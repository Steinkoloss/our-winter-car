namespace WinterMP.Net
{
    /// <summary>PlayerTemp is native body warmth, not an ambient Celsius reading.</summary>
    public static class PlayerWarmthPolicy
    {
        // Native warmth can reach zero/below zero in the cold. Availability is
        // explicit so neither zero nor an old air-temperature column is a sentinel.
        public static bool Valid(bool available, float value) => available
            ? !float.IsNaN(value) && !float.IsInfinity(value)
            : value == 0f;
    }
}
