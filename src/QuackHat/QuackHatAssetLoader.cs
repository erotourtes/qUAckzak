using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DuckGame;
using Microsoft.Xna.Framework.Graphics;

namespace qUAckzak.Mod.QuackHat
{
    internal static class QuackHatAssetLoader
    {
        public static void Load(QuackHatDefinition hat)
        {
            try
            {
                foreach (QuackHatComponentDefinition component in hat.Components)
                {
                    LoadComponent(hat.Id, component);
                }
            }
            catch
            {
                Unload(hat);
                throw;
            }
        }

        public static void Unload(QuackHatDefinition hat)
        {
            foreach (QuackHatComponentDefinition component in hat.Components)
            {
                component.SpriteTexture?.Dispose();
                component.SpriteTexture = null;
            }
        }

        private static void LoadComponent(
            string hatId,
            QuackHatComponentDefinition component)
        {
            string context = $"component '{component.Id}' sprite";
            Texture2D texture;

            try
            {
                using FileStream stream = File.OpenRead(component.SpritePath);
                texture = ContentPack.LoadTexture2DFromStream(stream, processPink: true);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"{context} '{Path.GetFileName(component.SpritePath)}' could not be loaded as a PNG: {exception.Message}",
                    exception);
            }

            if (texture == null)
            {
                throw new InvalidDataException(
                    $"{context} '{Path.GetFileName(component.SpritePath)}' produced no texture.");
            }

            try
            {
                List<int> referencedFrames = component.Animations
                    .Select(animation => animation.LastFrame)
                    .ToList();
                QuackHatSpriteSheetLayout layout =
                    QuackHatSpriteSheetLayoutValidator.Validate(
                        texture.Width,
                        texture.Height,
                        component.FrameWidth,
                        component.FrameHeight,
                        referencedFrames,
                        context);

                texture.Name = $"qUAckhat/{hatId}/{component.Id}";
                component.SpriteTexture = texture;
                component.SheetWidth = texture.Width;
                component.SheetHeight = texture.Height;
                component.FrameWidth = layout.FrameWidth;
                component.FrameHeight = layout.FrameHeight;
                component.FrameCount = layout.FrameCount;
            }
            catch
            {
                texture.Dispose();
                throw;
            }
        }
    }
}
