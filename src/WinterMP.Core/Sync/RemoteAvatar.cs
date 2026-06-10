using UnityEngine;

namespace WinterMP.Core.Sync
{
    /// <summary>
    /// Visible body for a remote player: a tinted capsule with a floating name tag,
    /// smoothed toward the latest network snapshot. Placeholder until M5 swaps in a
    /// proper character model — but everything downstream (transform stream,
    /// interpolation, spawn/despawn) is the real thing.
    /// </summary>
    public sealed class RemoteAvatar : MonoBehaviour
    {
        private const float SnapDistance = 15f;
        private const float PositionSmoothing = 12f;
        private const float RotationSmoothing = 10f;

        /// <summary>Seat triggers sit at cushion height; avatar pivot is at the feet.</summary>
        private const float SeatFootOffset = 0.8f;

        /// <summary>Unity's capsule primitive is 2 m tall and 1 m wide at scale 1.</summary>
        private const float DefaultCapsuleHeight = 2f;
        private const float BodyWidthScale = 0.5f;
        private const float BodyHeightScale = 2f / 3f;
        /// <summary>Crouching removes another third of standing avatar height.</summary>
        private const float CrouchHeightFactor = 2f / 3f;
        private const float NameTagClearance = 0.25f;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation = Quaternion.identity;
        private Renderer? _bodyRenderer;
        private Transform? _bodyTransform;
        private TextMesh? _nameTag;
        private Transform? _nameTagTransform;
        private bool _hasTarget;
        private bool _crouching;

        // While driving, the avatar is pinned to the vehicle's seat instead of
        // chasing the (smoothed, lagging) world-space transform stream.
        private Transform? _anchorSeat;
        private Transform? _anchorVehicle;

        public static RemoteAvatar Create(byte playerId, string playerName)
        {
            var root = new GameObject($"WinterMP_Avatar_{playerId}");
            var avatar = root.AddComponent<RemoteAvatar>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.parent = root.transform;
            Destroy(body.GetComponent<Collider>()); // visual only — must not push the world around

            avatar._bodyTransform = body.transform;
            avatar._bodyRenderer = body.GetComponent<Renderer>();
            if (avatar._bodyRenderer != null)
                avatar._bodyRenderer.material.color = ColorForPlayer(playerId);
            avatar.ApplyBodyScale();

            var tag = new GameObject("NameTag");
            tag.transform.parent = root.transform;
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

            avatar._nameTag = text;
            avatar._nameTagTransform = tag.transform;
            root.SetActive(false); // until the first snapshot arrives
            return avatar;
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
            bool crouching = (moveState & PlayerMoveState.Crouch) != 0;
            if (_crouching == crouching) return;
            _crouching = crouching;
            ApplyBodyScale();
        }

        /// <summary>Pin the avatar into a vehicle (remote player is driving it).</summary>
        public void SetAnchor(Transform? seat, Transform? vehicle)
        {
            _anchorSeat = seat;
            _anchorVehicle = vehicle;
        }

        public void ClearAnchor()
        {
            _anchorSeat = null;
            _anchorVehicle = null;
        }

        private void Update()
        {
            if (!_hasTarget) return;

            if (_anchorSeat != null && _anchorVehicle != null)
            {
                // Hard-follow the vehicle: both it and this avatar are smoothed
                // copies; chaining a second lerp would visibly trail the cabin.
                transform.position = _anchorSeat.position - _anchorVehicle.up * SeatFootOffset;
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

            // Billboard the name tag. TextMesh faces +Z, so look away from the camera.
            var camera = Camera.main;
            if (camera != null && _nameTagTransform != null)
                _nameTagTransform.rotation = Quaternion.LookRotation(_nameTagTransform.position - camera.transform.position);
        }

        private void ApplyBodyScale()
        {
            if (_bodyTransform == null) return;

            float heightScale = BodyHeightScale;
            if (_crouching)
                heightScale *= CrouchHeightFactor;

            _bodyTransform.localScale = new Vector3(BodyWidthScale, heightScale, BodyWidthScale);

            // Capsule pivot is its center; player positions are at the feet.
            float height = DefaultCapsuleHeight * heightScale;
            _bodyTransform.localPosition = new Vector3(0f, height * 0.5f, 0f);

            if (_nameTagTransform != null)
                _nameTagTransform.localPosition = new Vector3(0f, height + NameTagClearance, 0f);
        }

        private static Color ColorForPlayer(byte playerId)
        {
            // Stable, distinct tints; golden-ratio hue stepping.
            float hue = (playerId * 0.618034f) % 1f;
            return HsvToRgb(hue, 0.65f, 0.9f);
        }

        private static Color HsvToRgb(float h, float s, float v)
        {
            // Unity 5.0 has no Color.HSVToRGB yet.
            float r = Mathf.Abs(h * 6f - 3f) - 1f;
            float g = 2f - Mathf.Abs(h * 6f - 2f);
            float b = 2f - Mathf.Abs(h * 6f - 4f);
            var rgb = new Vector3(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
            rgb = Vector3.Lerp(Vector3.one, rgb, s) * v;
            return new Color(rgb.x, rgb.y, rgb.z);
        }
    }
}
