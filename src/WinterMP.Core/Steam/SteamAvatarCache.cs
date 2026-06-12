#if STEAMWORKS
using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace WinterMP.Core.Steam
{
    /// <summary>Caches Steam friend avatars as Unity textures for IMGUI lobby UI.</summary>
    internal static class SteamAvatarCache
    {
        private static readonly Dictionary<ulong, Texture2D> Cache = new Dictionary<ulong, Texture2D>();
        private static readonly HashSet<ulong> Pending = new HashSet<ulong>();
        private static Callback<AvatarImageLoaded_t>? _avatarLoaded;
        private static bool _registered;

        public static void EnsureRegistered()
        {
            if (_registered) return;
            if (!SteamBootstrap.EnsureInitialized()) return;

            _avatarLoaded = Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);
            _registered = true;
        }

        public static Texture2D? TryGet(ulong steamId)
        {
            if (steamId == 0) return null;

            EnsureRegistered();
            if (Cache.TryGetValue(steamId, out Texture2D cached))
                return cached;

            try
            {
                int image = SteamFriends.GetLargeFriendAvatar(new CSteamID(steamId));
                if (image <= 0)
                {
                    Pending.Add(steamId);
                    return null;
                }

                Texture2D? tex = ImageToTexture(image);
                if (tex != null)
                    Cache[steamId] = tex;
                return tex;
            }
            catch (Exception e)
            {
                WinterMPPlugin.Log.LogWarning($"Steam avatar fetch failed for {steamId}: {e.Message}");
                return null;
            }
        }

        private static void OnAvatarLoaded(AvatarImageLoaded_t param)
        {
            ulong steamId = param.m_steamID.m_SteamID;
            if (param.m_iImage <= 0) return;

            Texture2D? tex = ImageToTexture(param.m_iImage);
            if (tex == null) return;

            Cache[steamId] = tex;
            Pending.Remove(steamId);
        }

        private static Texture2D? ImageToTexture(int image)
        {
            uint width;
            uint height;
            if (!SteamUtils.GetImageSize(image, out width, out height) || width == 0 || height == 0)
                return null;

            int pixelCount = (int)(width * height);
            byte[] rgba = new byte[pixelCount * 4];
            if (!SteamUtils.GetImageRGBA(image, rgba, rgba.Length))
                return null;

            FlipVertical(rgba, (int)width, (int)height);

            var tex = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
            tex.LoadRawTextureData(rgba);
            tex.Apply();
            return tex;
        }

        private static void FlipVertical(byte[] rgba, int width, int height)
        {
            int rowBytes = width * 4;
            byte[] row = new byte[rowBytes];
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * rowBytes;
                int bottom = (height - 1 - y) * rowBytes;
                Array.Copy(rgba, top, row, 0, rowBytes);
                Array.Copy(rgba, bottom, rgba, top, rowBytes);
                Array.Copy(row, 0, rgba, bottom, rowBytes);
            }
        }
    }
}
#else
using UnityEngine;

namespace WinterMP.Core.Steam
{
    internal static class SteamAvatarCache
    {
        public static void EnsureRegistered() { }

        public static Texture2D? TryGet(ulong steamId) => null;
    }
}
#endif
