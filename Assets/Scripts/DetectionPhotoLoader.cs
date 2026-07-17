using System;
using System.IO;
using UnityEngine;

public static class DetectionPhotoLoader
{
    public static bool TryLoadSprite(
        string filePath,
        out Texture2D texture,
        out Sprite sprite
    )
    {
        texture = null;
        sprite = null;

        if (string.IsNullOrWhiteSpace(filePath) ||
            !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            byte[] imageBytes = File.ReadAllBytes(filePath);

            texture = new Texture2D(
                2,
                2,
                TextureFormat.RGB24,
                false
            );

            if (!texture.LoadImage(imageBytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
                return false;
            }

            texture.name = "DetectionPhotoTexture";

            sprite = Sprite.Create(
                texture,
                new Rect(
                    0f,
                    0f,
                    texture.width,
                    texture.height
                ),
                new Vector2(0.5f, 0.5f),
                100f
            );

            sprite.name = "DetectionPhotoSprite";
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                "[DetectionPhotoLoader] Could not load " +
                filePath + ": " + e.Message
            );

            if (sprite != null)
                UnityEngine.Object.Destroy(sprite);

            if (texture != null)
                UnityEngine.Object.Destroy(texture);

            sprite = null;
            texture = null;
            return false;
        }
    }

    public static void DestroyLoaded(
        ref Texture2D texture,
        ref Sprite sprite
    )
    {
        if (sprite != null)
        {
            UnityEngine.Object.Destroy(sprite);
            sprite = null;
        }

        if (texture != null)
        {
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }
}
