using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Visible body for a remote player: a cloned HUMANS walker rig (or capsule
    /// fallback) with a floating name tag, smoothed toward the latest network
    /// snapshot and animated from streamed <see cref="PlayerMoveState"/> flags.
    /// </summary>
    public sealed class RemoteAvatar : MonoBehaviour
    {
        private const float SnapDistance = 15f;
        private const float PositionSmoothing = 12f;
        private const float RotationSmoothing = 10f;

        /// <summary>Seat triggers sit at cushion height; avatar pivot is at the feet.</summary>
        private const float SeatFootOffset = 0.8f;
        private const float SeatedFootOffset = 0.55f;

        private const float DefaultCapsuleHeight = 2f;
        private const float BodyWidthScale = 0.5f;
        private const float BodyHeightScale = 2f / 3f;
        private const float CrouchHeightFactor = 2f / 3f;
        private const float NameTagClearance = 0.25f;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private Renderer? _bodyRenderer;
        private Transform? _bodyTransform;
        private TextMesh? _nameTag;
        private Transform? _nameTagTransform;
        private RemoteCharacterAnimator? _characterAnimator;
        private Transform? _rigRoot;
        private Transform? _rigPivot;
        private Transform? _lookTarget;
        private Vector3 _rigFootOffset;
        private Vector3 _pivotBaseLocalPos;
        private Quaternion _pivotBaseLocalRot = Quaternion.identity;
        /// <summary>NPC mesh forward axis relative to avatar +Z (MWC walkers face -Z).</summary>
        private const float ModelForwardSign = -1f;
        private static readonly Vector3 LookTargetLocalOffset = new Vector3(0f, 1.2f, 2f);
        private bool _hasTarget;
        private bool _crouching;
        private bool _vehicleAnchored;
        private byte _moveState;

        private Transform? _anchorSeat;
        private Transform? _anchorVehicle;

        public static RemoteAvatar Create(byte playerId, string playerName)
        {
            var root = new GameObject($"WinterMP_Avatar_{playerId}");
            var avatar = root.AddComponent<RemoteAvatar>();

            var rig = NpcCharacterFactory.TryCreate(playerId);
            if (rig != null)
            {
                rig.Root.transform.parent = root.transform;
                rig.Root.transform.localRotation = Quaternion.identity;
                avatar._rigRoot = rig.Root.transform;
                avatar._rigPivot = rig.Pivot;
                avatar._lookTarget = rig.LookTarget;
                avatar._rigFootOffset = new Vector3(0f, rig.FootOffsetY, 0f);
                avatar._pivotBaseLocalPos = rig.PivotBaseLocalPos;
                avatar._pivotBaseLocalRot = rig.PivotBaseLocalRot;
                avatar._rigRoot.localPosition = avatar._rigFootOffset;

                if (avatar._lookTarget != null)
                {
                    avatar._lookTarget.parent = root.transform;
                    avatar._lookTarget.localPosition = new Vector3(
                        LookTargetLocalOffset.x,
                        LookTargetLocalOffset.y,
                        LookTargetLocalOffset.z * ModelForwardSign);
                }

                avatar._characterAnimator = new RemoteCharacterAnimator(rig);
                avatar._bodyTransform = rig.BodyPivot;
            }
            else
            {
                avatar.CreateCapsuleFallback(playerId);
            }

            avatar.CreateNameTag(playerName);
            root.SetActive(false);
            return avatar;
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
            _targetRotation = rotation;

            if (!_hasTarget)
            {
                _hasTarget = true;
                transform.position = position;
                transform.rotation = rotation;
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
                transform.rotation = _anchorVehicle.rotation;
            }
            else if (Vector3.Distance(transform.position, _targetPosition) > SnapDistance)
            {
                transform.position = _targetPosition;
                transform.rotation = _targetRotation;
            }
            else
            {
                float positionT = 1f - Mathf.Exp(-PositionSmoothing * Time.deltaTime);
                float rotationT = 1f - Mathf.Exp(-RotationSmoothing * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, _targetPosition, positionT);
                transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, rotationT);
            }

            PinRigRoot();
            _characterAnimator?.Tick();

            var camera = Camera.main;
            if (camera != null && _nameTagTransform != null)
                _nameTagTransform.rotation = Quaternion.LookRotation(_nameTagTransform.position - camera.transform.position);
        }

        private void PinRigRoot()
        {
            if (_rigRoot == null) return;
            _rigRoot.localPosition = _rigFootOffset;
            _rigRoot.localRotation = Quaternion.identity;

            if (_rigPivot != null)
            {
                _rigPivot.localPosition = _pivotBaseLocalPos;
                _rigPivot.localRotation = _pivotBaseLocalRot;
            }
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
