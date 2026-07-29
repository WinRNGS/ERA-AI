using SkiaSharp;
using System.Collections.Generic;
using System.IO;

namespace MinorShift.Emuera.UI.Game.Image
{
    public static class AnimatedImageHelper
    {
        public static bool GetAnimInfo(string filepath, out int width, out int height, out int frameCount, out int[] delays)
        {
            width = height = frameCount = 0;
            delays = null;

            if (!File.Exists(filepath)) return false;

            using var codec = SKCodec.Create(filepath);
            if (codec == null) return false;

            width = codec.Info.Width;
            height = codec.Info.Height;
            frameCount = codec.FrameCount;

            if (frameCount > 1)
            {
                delays = new int[frameCount];
                for (int i = 0; i < frameCount; i++)
                {
                    int duration = codec.FrameInfo[i].Duration;
                    delays[i] = duration > 0 ? duration : 100;
                }
                return true;
            }
            return false;
        }

        public static List<(SKBitmap Bitmap, int Delay)> Decode(string filepath)
        {
            if (!File.Exists(filepath)) return null;

            using var codec = SKCodec.Create(filepath);
            if (codec == null || codec.FrameCount <= 1)
                return null;

            var frames = new List<(SKBitmap, int)>();
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height);

            // 用于保存已解码的完整帧，供后续帧作为 RequiredFrame 依赖使用
            var decodedFrames = new SKBitmap[codec.FrameCount];

            // 唯一的工作缓冲区，SkCodec 会直接在这个缓冲区上进行增量绘制和混合
            using var bitmap = new SKBitmap(info);
            var ptr = bitmap.GetPixels();

            for (int i = 0; i < codec.FrameCount; i++)
            {
                var frameInfo = codec.FrameInfo[i];
                int delay = frameInfo.Duration > 0 ? frameInfo.Duration : 100;

                // 获取当前帧依赖的前置帧（SkiaSharp 已经帮我们算好了 RestorePrevious 等复杂逻辑）
                int reqFrame = frameInfo.RequiredFrame;
                if (reqFrame != -1 && decodedFrames[reqFrame] != null)
                {
                    // 将依赖帧的画面拷贝到工作缓冲区
                    using var canvas = new SKCanvas(bitmap);
                    using var copyPaint = new SKPaint { BlendMode = SKBlendMode.Src };
                    canvas.DrawBitmap(decodedFrames[reqFrame], 0, 0, copyPaint);

                    // 如果依赖帧的处置方式是恢复背景色，我们需要在解码当前帧前，把依赖帧的区域擦除透明
                    var reqFrameInfo = codec.FrameInfo[reqFrame];
                    if (reqFrameInfo.DisposalMethod == SKCodecAnimationDisposalMethod.RestoreBackgroundColor)
                    {
                        using var clearPaint = new SKPaint { BlendMode = SKBlendMode.Src, Color = SKColors.Transparent };
                        var rect = SKRect.Create(reqFrameInfo.FrameRect.Left, reqFrameInfo.FrameRect.Top, reqFrameInfo.FrameRect.Width, reqFrameInfo.FrameRect.Height);
                        canvas.DrawRect(rect, clearPaint);
                    }
                }
                else
                {
                    // 如果没有依赖帧（如第0帧），清空工作缓冲区
                    bitmap.Erase(SKColors.Transparent);
                }

                // 核心优化：传入 reqFrame，告诉 SkCodec 缓冲区里已经准备好了前置画面
                // SkCodec 会自动处理增量解码和 BlendMode 混合，耗时降至 O(1)
                var options = reqFrame == -1 ? new SKCodecOptions(i) : new SKCodecOptions(i, reqFrame);
                codec.GetPixels(info, ptr, options);

                // 拷贝出最终合成的当前帧画面并保存
                var frameCopy = bitmap.Copy();
                decodedFrames[i] = frameCopy;
                frames.Add((frameCopy, delay));
            }

            return frames;
        }
    }
}