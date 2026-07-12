using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Visible body for a remote player: cloned NPC mesh with look yaw on the avatar
    /// root and skeletal walk cycles from a hidden Move-FSM driver.
    /// </summary>
    public sealed class RemoteAvatar : MonoBehaviour
    {
        private const float SnapDistance = 15f;
        private const float PositionSmoothing = 12f;
        private const float RotationSmoothing = 14f;

        private const float SeatFootOffset = 0.8f;
        private const float SeatedFootOffset = 0.55f;

        private const float DefaultCapsuleHeight = 2f;
        private const float BodyWidthScale = 0.5f;
        private const float BodyHeightScale = 2f / 3f;
        private const float CrouchHeightFactor = 2f / 3f;
        private const float NameTagClearance = 0.25f;

        private static readonly Vector3 LookTargetLocalOffset = new Vector3(0f, 1.2f, 2f);

        private Vector3 _targetPosition;
        private float _targetYaw;
        private Renderer? _bodyRenderer;
        private Transform? _bodyTransform;
        private TextMesh? _nameTag;
        private Transform? _nameTagTransform;
        private RemoteCharacterAnimator? _characterAnimator;
        private Transform? _rigRoot;
        private Transform? _moveDriver;
        private Transform? _driverPivot;
        private Transform? _lookTarget;
        private Vector3 _rigFootOffset;
        private Vector3 _driverPivotBaseLocalPos;
        private Quaternion _driverPivotBaseLocalRot = Quaternion.identity;
        private float _modelYawOffset;
        private bool _hasTarget;
        private bool _crouching;
        private bool _vehicleAnchored;
        private byte _moveState;

        private Transform? _anchorSeat;
        private Transform? _anchorVehicle;

        private const byte ClothingUnset = 255;
        private byte _clothingStage = ClothingUnset;
        private byte _clothingType = ClothingUnset;
        private Renderer[]? _clothingRenderers;

        public static RemoteAvatar Create(byte playerId, string playerName)
        {
            var root = new GameObject($"WinterMP_Avatar_{playerId}");
            var avatar = root.AddComponent<RemoteAvatar>();
            avatar.BuildBody(playerId);
            avatar.CreateNameTag(playerName);
            root.SetActive(false);
            return avatar;
        }

        /// <summary>Minimal visible fallback when the NPC rig fails to build.</summary>
        public static RemoteAvatar CreateCapsule(byte playerId, string playerName)
        {
            var root = new GameObject($"WinterMP_Avatar_{playerId}");
            var avatar = root.AddComponent<RemoteAvatar>();
            avatar.CreateCapsuleFallback(playerId);
            avatar.CreateNameTag(playerName);
            root.SetActive(false);
            return avatar;
        }

        private void BuildBody(byte playerId)
        {
            var rig = NpcCharacterFactory.TryCreate(playerId);
            if (rig != null)
            {
                rig.Root.transform.parent = transform;
                _rigRoot = rig.Root.transform;
                _rigFootOffset = new Vector3(0f, rig.FootOffsetY, 0f);
                _modelYawOffset = rig.ModelYawOffset;
                _rigRoot.localPosition = _rigFootOffset;
                _rigRoot.localRotation = Quaternion.Euler(0f, _modelYawOffset, 0f);

                if (rig.MoveDriver != null)
                {
                    rig.MoveDriver.transform.parent = transform;
                    _moveDriver = rig.MoveDriver.transform;
                    _driverPivot = rig.DriverPivot;
                    _driverPivotBaseLocalPos = rig.DriverPivotBaseLocalPos;
                    _driverPivotBaseLocalRot = rig.DriverPivotBaseLocalRot;
                    _moveDriver.localPosition = _rigFootOffset;
                    _moveDriver.localRotation = Quaternion.Euler(0f, _modelYawOffset, 0f);

                    _lookTarget = rig.LookTarget;
                    if (_lookTarget != null)
                    {
                        _lookTarget.parent = transform;
                        _lookTarget.localPosition = LookTargetLocalOffset;
                        NpcCharacterFactory.BindMoveTarget(rig.MoveFsm, _lookTarget.gameObject);
                    }
                }

                _characterAnimator = new RemoteCharacterAnimator(rig);
                _bodyTransform = rig.BodyPivot;
                EnsureRenderersEnabled(rig.Root);
                return;
            }

            CreateCapsuleFallback(playerId);
        }

        private static void EnsureRenderersEnabled(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                    renderers[i].enabled = true;
            }
        }

        private void CreateCapsuleFallback(byte playerId)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.parent = transform;
            Destroy(body.GetComponent<Collider>());

            _bodyTransform = body.transform;
            _bodyRenderer = body.GetComponent<Renderer>();
            if (_bodyRenderer != null)
                _bodyRenderer.material.color = ColorForPlayer(playerId);
            ApplyCapsuleScale();
        }

        private void CreateNameTag(string playerName)
        {
            var tag = new GameObject("NameTag");
            tag.transform.parent = transform;
            var text = tag.AddComponent<TextMesh>();
            text.text = playerName;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.06f;
            text.fontSize = 64;
            text.color = Color.white;
            var font = Resources.GetBuiltinResource(typeof(Font), "Arial.ttf") as Font;
            if (font != null)
            {
                text.font = font;
                var tagRenderer = tag.GetComponent<Renderer>();
                if (tagRenderer != null) tagRenderer.material = font.material;
            }

            _nameTag = text;
            _nameTagTransform = tag.transform;
            UpdateNameTagPosition();
        }

        public void SetTarget(Vector3 position, Quaternion rotation)
        {
            _targetPosition = position;
            _targetYaw = YawFromRotation(rotation);

            if (!_hasTarget)
            {
                _hasTarget = true;
                transform.position = position;
                ApplyLookYaw(_targetYaw);
                gameObject.SetActive(true);
            }
        }

        public void SetVisible(bool visible)
        {
            if (_hasTarget && gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        public void SetMoveState(byte moveState)
        {
            _moveState = moveState;
            ApplyMoveState();
        }

        public void SetAnchor(Transform? seat, Transform? vehicle)
        {
            if (_anchorSeat == seat && _anchorVehicle == vehicle)
                return;

            _anchorSeat = seat;
            _anchorVehicle = vehicle;
            _vehicleAnchored = seat != null && vehicle != null;
            ApplyMoveState();
        }

        public void ClearAnchor() => SetAnchor(null, null);

        /// <summary>
        /// Apply a remote player's worn clothing. State is always stored (authoritative);
        /// the visual is best-effort. The avatar clone has all PlayMaker FSMs stripped
        /// (NpcCharacterFactory.StripVisualSimulation), so the in-game SetMaterials /
        /// CLOTHESHOME-CLOTHESWORK path is unavailable here — we tint the shirt material by
        /// warmth stage instead (warmer stage = darker/heavier look) so dressing up/down is
        /// visible. Fully crash-contained; no-op if renderers are unavailable.
        /// </summary>
        public void SetClothing(byte stage, byte type)
        {
            if (_clothingStage == stage && _clothingType == type) return;
            _clothingStage = stage;
            _clothingType = type;

            try
            {
                ApplyClothingTint();
            }
            catch (System.Exception e)
            {
                WinterMPPlugin.Log.LogDebug("RemoteAvatar: clothing visual failed: " + e.Message);
            }
        }

        private void ApplyClothingTint()
        {
            if (_clothingRenderers == null)
            {
                _clothingRenderers = _bodyRenderer != null
                    ? new[] { _bodyRenderer }
                    : GetComponentsInChildren<Renderer>(true);
            }

            if (_clothingRenderers == null || _clothingRenderers.Length == 0) return;

            // Approximate: darker as the warmth stage climbs. Stages are small enum indices;
            // clamp to a sane span so a stray value can't blow the brightness out.
            float darkness = Mathf.Clamp01(_clothingStage / 6f);
            float brightness = Mathf.Lerp(1f, 0.55f, darkness);
            var tint = new Color(brightness, brightness, brightness);

            for (int i = 0; i < _clothingRenderers.Length; i++)
            {
                var renderer = _clothingRenderers[i];
                if (renderer == null) continue;

                var material = renderer.material;
                if (material != null && material.HasProperty("_Color"))
                    material.color = tint;
            }
        }

        private void ApplyMoveState()
        {
            if (_characterAnimator != null)
            {
                _characterAnimator.Apply(_moveState, _vehicleAnchored);
                UpdateNameTagPosition();
                UpdateNameTagVisibility();
                return;
            }

            bool crouching = PlayerMoveState.Has(_moveState, PlayerMoveState.Crouch);
            if (_crouching == crouching) return;
            _crouching = crouching;
            ApplyCapsuleScale();
            UpdateNameTagVisibility();
        }

        private void Update()
        {
            if (!_hasTarget) return;

            if (_anchorSeat != null && _anchorVehicle != null)
            {
                float footOffset = _vehicleAnchored ? SeatedFootOffset : SeatFootOffset;
                transform.position = _anchorSeat.position - _anchorVehicle.up * footOffset;
                ApplyLookYaw(_anchorVehicle.eulerAngles.y);
            }
            else if (Vector3.Distance(transform.position, _targetPosition) > SnapDistance)
            {
                transform.position = _targetPosition;
                ApplyLookYaw(_targetYaw);
            }
            else
            {
                float positionT = 1f - Mathf.Exp(-PositionSmoothing * Time.deltaTime);
                float rotationT = 1f - Mathf.Exp(-RotationSmoothing * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, _targetPosition, positionT);

                float yaw = Mathf.LerpAngle(transform.eulerAngles.y, _targetYaw, rotationT);
                ApplyLookYaw(yaw);
            }

            PinRigRoot();

            var camera = Camera.main;
            if (camera != null && _nameTagTransform != null)
                _nameTagTransform.rotation = Quaternion.LookRotation(_nameTagTransform.position - camera.transform.position);
        }

        private void LateUpdate()
        {
            if (!_hasTarget) return;

            bool walking = _characterAnimator != null && _characterAnimator.IsWalking;
            PinMoveDriver(walking);
            _characterAnimator?.Tick();
            PinRigRoot();
        }

        private void ApplyLookYaw(float yawDegrees)
        {
            transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
        }

        private void PinRigRoot()
        {
            if (_rigRoot != null)
            {
                _rigRoot.localPosition = _rigFootOffset;
                _rigRoot.localRotation = Quaternion.Euler(0f, _modelYawOffset, 0f);
            }
        }

        private void PinMoveDriver(bool walking)
        {
            if (_moveDriver == null) return;
            if (walking) return;

            _moveDriver.localPosition = _rigFootOffset;
            _moveDriver.localRotation = Quaternion.Euler(0f, _modelYawOffset, 0f);

            if (_driverPivot != null)
            {
                _driverPivot.localPosition = _driverPivotBaseLocalPos;
                _driverPivot.localRotation = _driverPivotBaseLocalRot;
            }
        }

        private static float YawFromRotation(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return rotation.eulerAngles.y;

            return Quaternion.LookRotation(forward.normalized).eulerAngles.y;
        }

        private void ApplyCapsuleScale()
        {
            if (_bodyTransform == null) return;

            float heightScale = BodyHeightScale;
            if (_crouching)
                heightScale *= CrouchHeightFactor;

            _bodyTransform.localScale = new Vector3(BodyWidthScale, heightScale, BodyWidthScale);

            float height = DefaultCapsuleHeight * heightScale;
            _bodyTransform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            UpdateNameTagPosition();
        }

        private void UpdateNameTagPosition()
        {
            if (_nameTagTransform == null) return;

            float height = _characterAnimator != null
                ? _characterAnimator.NameTagHeight
                : DefaultCapsuleHeight * BodyHeightScale * (_crouching ? CrouchHeightFactor : 1f);

            _nameTagTransform.localPosition = new Vector3(0f, height + NameTagClearance, 0f);
        }

        private void UpdateNameTagVisibility()
        {
            if (_nameTagTransform == null) return;
            bool hide = PlayerMoveState.Has(_moveState, PlayerMoveState.Swimming);
            if (_nameTagTransform.gameObject.activeSelf == !hide) return;
            _nameTagTransform.gameObject.SetActive(!hide);
        }

        private static Color ColorForPlayer(byte playerId)
        {
            float hue = (playerId * 0.618034f) % 1f;
            return HsvToRgb(hue, 0.65f, 0.9f);
        }

        private static Color HsvToRgb(float h, float s, float v)
        {
            float r = Mathf.Abs(h * 6f - 3f) - 1f;
            float g = 2f - Mathf.Abs(h * 6f - 2f);
            float b = 2f - Mathf.Abs(h * 6f - 4f);
            var rgb = new Vector3(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
            rgb = Vector3.Lerp(Vector3.one, rgb, s) * v;
            return new Color(rgb.x, rgb.y, rgb.z);
        }
    }
}
