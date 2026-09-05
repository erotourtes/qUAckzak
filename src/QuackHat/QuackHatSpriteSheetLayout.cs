using System;
using System.Collections.Generic;
using System.IO;

namespace qUAckzak.Mod.QuackHat
{
    internal readonly struct QuackHatSpriteSheetLayout
    {
        public QuackHatSpriteSheetLayout(int frameWidth, int frameHeight, int frameCount)
        {
            FrameWidth = frameWidth;
            FrameHeight = frameHeight;
            FrameCount = frameCount;
        }

        public int FrameWidth { get; }

        public int FrameHeight { get; }

        public int FrameCount { get; }
    }

    internal static class QuackHatSpriteSheetLayoutValidator
    {
        public static QuackHatSpriteSheetLayout Validate(
            int sheetWidth,
            int sheetHeight,
            int declaredFrameWidth,
            int declaredFrameHeight,
            IReadOnlyList<int> referencedFrames,
            string context)
        {
            if (sheetWidth <= 0 || sheetHeight <= 0)
            {
                throw Error(context, "must have positive image dimensions.");
            }

            bool isAnimated = declaredFrameWidth > 0 && declaredFrameHeight > 0;
            int frameWidth = isAnimated ? declaredFrameWidth : sheetWidth;
            int frameHeight = isAnimated ? declaredFrameHeight : sheetHeight;

            if (frameWidth > sheetWidth || frameHeight > sheetHeight)
            {
                throw Error(
                    context,
                    $"declares {frameWidth}x{frameHeight} frames but the image is only {sheetWidth}x{sheetHeight} pixels.");
            }

            if (sheetWidth % frameWidth != 0 || sheetHeight % frameHeight != 0)
            {
                throw Error(
                    context,
                    $"image size {sheetWidth}x{sheetHeight} is not evenly divisible by its {frameWidth}x{frameHeight} frame size.");
            }

            long frameCount = (long)(sheetWidth / frameWidth) * (sheetHeight / frameHeight);
            if (frameCount > int.MaxValue)
            {
                throw Error(context, "contains more frames than qUAckhat can address.");
            }

            foreach (int frame in referencedFrames)
            {
                if (frame >= frameCount)
                {
                    throw Error(
                        context,
                        $"references frame {frame}, but the image contains only {frameCount} frame(s) numbered 0 through {frameCount - 1}.");
                }
            }

            return new QuackHatSpriteSheetLayout(
                frameWidth,
                frameHeight,
                (int)frameCount);
        }

        private static InvalidDataException Error(string context, string message)
        {
            return new InvalidDataException($"{context} {message}");
        }
    }
}
