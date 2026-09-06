using System;
using System.Collections.Generic;
using System.IO;
using DuckGame;
using Microsoft.Xna.Framework.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace qUAckzak.Mod.QuackHat
{
    internal static class QuackHatNetworkAssetRegistry
    {
        private const int NativeHatSize = 32;

        public static void Register(QuackHatDefinition hat)
        {
            int requiredTeams = 0;
            foreach (QuackHatComponentDefinition component in hat.Components)
            {
                requiredTeams += GetTileCount(component) * component.FrameCount;
            }

            int availableTeams = Teams.kCustomSpread - Teams.core.extraTeams.Count;
            if (requiredTeams > availableTeams)
            {
                throw new InvalidDataException(
                    $"qUAckhat '{hat.Name}' needs {requiredTeams} hidden online hat images, "
                    + $"but Duck Game's custom-team range has room for {availableTeams}.");
            }

            try
            {
                foreach (QuackHatComponentDefinition component in hat.Components)
                {
                    RegisterComponent(hat, component);
                }
            }
            catch
            {
                Unregister(hat);
                throw;
            }
        }

        public static void Unregister(QuackHatDefinition hat)
        {
            foreach (QuackHatComponentDefinition component in hat.Components)
            {
                if (component.NetworkTiles == null)
                {
                    continue;
                }

                foreach (QuackHatNetworkTileDefinition tile in component.NetworkTiles)
                {
                    foreach (Team team in tile.FrameTeams)
                    {
                        Teams.core.extraTeams.Remove(team);
                    }
                }

                component.NetworkTiles = null;
            }

            HatSelector.remember = null;
        }

        private static void RegisterComponent(
            QuackHatDefinition hat,
            QuackHatComponentDefinition component)
        {
            int tilesWide = DivideRoundUp(component.FrameWidth, NativeHatSize);
            int tilesHigh = DivideRoundUp(component.FrameHeight, NativeHatSize);
            int paddedWidth = tilesWide * NativeHatSize;
            int paddedHeight = tilesHigh * NativeHatSize;
            int paddingLeft = (paddedWidth - component.FrameWidth) / 2;
            int paddingTop = (paddedHeight - component.FrameHeight) / 2;
            int columns = component.SheetWidth / component.FrameWidth;

            XnaColor[] sheetPixels = new XnaColor[component.SheetWidth * component.SheetHeight];
            component.SpriteTexture.GetData(sheetPixels);

            List<Team>[,] frameTeams = new List<Team>[tilesWide, tilesHigh];
            List<Team> registeredTeams = new();
            for (int tileY = 0; tileY < tilesHigh; tileY++)
            {
                for (int tileX = 0; tileX < tilesWide; tileX++)
                {
                    frameTeams[tileX, tileY] = new List<Team>(component.FrameCount);
                }
            }

            try
            {
                for (int frame = 0; frame < component.FrameCount; frame++)
                {
                    int frameX = frame % columns * component.FrameWidth;
                    int frameY = frame / columns * component.FrameHeight;

                    for (int tileY = 0; tileY < tilesHigh; tileY++)
                    {
                        for (int tileX = 0; tileX < tilesWide; tileX++)
                        {
                            XnaColor[] tilePixels = ExtractTile(
                                sheetPixels,
                                component,
                                frameX,
                                frameY,
                                tileX,
                                tileY,
                                paddingLeft,
                                paddingTop);
                            string name = $"DGRDD_qUAckhat_{hat.Id}_{component.Id}_{frame}_{tileX}_{tileY}";
                            Team team = CreateTransferableTeam(tilePixels, name);
                            Teams.AddExtraTeam(team);
                            registeredTeams.Add(team);
                            frameTeams[tileX, tileY].Add(team);
                        }
                    }
                }
            }
            catch
            {
                foreach (Team team in registeredTeams)
                {
                    Teams.core.extraTeams.Remove(team);
                }

                throw;
            }

            List<QuackHatNetworkTileDefinition> tiles = new(tilesWide * tilesHigh);
            for (int tileY = 0; tileY < tilesHigh; tileY++)
            {
                for (int tileX = 0; tileX < tilesWide; tileX++)
                {
                    tiles.Add(new QuackHatNetworkTileDefinition
                    {
                        OffsetX = tileX * NativeHatSize + NativeHatSize / 2f
                            - paddedWidth / 2f,
                        OffsetY = tileY * NativeHatSize + NativeHatSize / 2f
                            - paddedHeight / 2f,
                        FrameTeams = frameTeams[tileX, tileY]
                    });
                }
            }

            component.NetworkTiles = tiles;
        }

        private static XnaColor[] ExtractTile(
            XnaColor[] sheetPixels,
            QuackHatComponentDefinition component,
            int frameX,
            int frameY,
            int tileX,
            int tileY,
            int paddingLeft,
            int paddingTop)
        {
            XnaColor[] pixels = new XnaColor[NativeHatSize * NativeHatSize];
            for (int y = 0; y < NativeHatSize; y++)
            {
                int sourceY = tileY * NativeHatSize + y - paddingTop;
                if (sourceY < 0 || sourceY >= component.FrameHeight)
                {
                    continue;
                }

                for (int x = 0; x < NativeHatSize; x++)
                {
                    int sourceX = tileX * NativeHatSize + x - paddingLeft;
                    if (sourceX < 0 || sourceX >= component.FrameWidth)
                    {
                        continue;
                    }

                    pixels[y * NativeHatSize + x] = sheetPixels[
                        (frameY + sourceY) * component.SheetWidth + frameX + sourceX];
                }
            }

            return pixels;
        }

        private static Team CreateTransferableTeam(XnaColor[] pixels, string name)
        {
            using Texture2D texture = new(Graphics.device, NativeHatSize, NativeHatSize);
            texture.SetData(pixels);
            using MemoryStream stream = new();
            texture.SaveAsPng(stream, NativeHatSize, NativeHatSize);

            Team team = Team.DeserializeFromPNG(stream.ToArray(), name, null);
            if (team == null || team.customData == null)
            {
                throw new InvalidDataException(
                    $"Could not create Duck Game network image '{name}'.");
            }

            return team;
        }

        private static int GetTileCount(QuackHatComponentDefinition component)
        {
            return DivideRoundUp(component.FrameWidth, NativeHatSize)
                * DivideRoundUp(component.FrameHeight, NativeHatSize);
        }

        private static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
