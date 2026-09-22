using WinterMP.Net.Messages;

namespace WinterMP.Net.Sync
{
    public static class PartBeltVisualPolicy
    {
        public const float MinimumScale = .0999f, MaximumScale = 1.1001f;
        public const float MinimumPitch = 0f, MaximumPitch = 3f;
        public const float MinimumVolume = 0f, MaximumVolume = 1f;
        public const float MinimumScrollSpeed = -10000f, MaximumScrollSpeed = 10000f;

        public static bool Valid(PartBeltVisualState? state) => state != null
            && (!state.Running || state.Visible)
            && Range(state.Scale, MinimumScale, MaximumScale)
            && Range(state.Pitch, MinimumPitch, MaximumPitch)
            && Range(state.Volume, MinimumVolume, MaximumVolume)
            && Range(state.ScrollSpeed, MinimumScrollSpeed, MaximumScrollSpeed);

        public static bool Valid(ReplacementPartState state, bool supportsBeltVisual) => state.BeltVisual == null
            || (supportsBeltVisual && PartAttachmentPolicy.HasAttachment(state) && Valid(state.BeltVisual));

        public static PartBeltVisualState? Copy(PartBeltVisualState? state) => state == null ? null : new PartBeltVisualState {
            Visible = state.Visible, Running = state.Running, Scale = state.Scale, Pitch = state.Pitch,
            Volume = state.Volume, ScrollSpeed = state.ScrollSpeed };

        public static bool Same(PartBeltVisualState? a, PartBeltVisualState? b) => a == null ? b == null : b != null
            && a.Visible == b.Visible && a.Running == b.Running && a.Scale == b.Scale && a.Pitch == b.Pitch
            && a.Volume == b.Volume && a.ScrollSpeed == b.ScrollSpeed;

        private static bool Range(float value, float minimum, float maximum) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;
    }
}
