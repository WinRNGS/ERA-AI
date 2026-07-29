using System;
using System.Windows.Forms;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using OpenTK.Graphics.ES20;

namespace MinorShift.Emuera.UI.Framework.Forms
{
	internal sealed class EraPictureBox : SKGLControl
	{
		public static bool UseOpenGL { get; set; } = true;
		public static event Action OpenGLFailed;
		internal static int failureCount = 0;
		private const int MaxFailures = 3;

		private const SKColorType colorType = SKColorType.Rgba8888;
		private const GRSurfaceOrigin surfaceOrigin = GRSurfaceOrigin.BottomLeft;

		private GRContext grContext;
		private GRGlFramebufferInfo glInfo;
		private GRBackendRenderTarget renderTarget;
		private SKSurface surface;
		private SKCanvas canvas;
		private SKSizeI lastSize;

		public static string RenderingBackend => UseOpenGL ? "SkiaSharp (OpenGL)" : "SkiaSharp (CPU)";

		public EraPictureBox()
		{
			SetStyle(ControlStyles.Opaque, true);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			try
			{
				if (DesignMode)
				{
					e.Graphics.Clear(BackColor);
					return;
				}

				MakeCurrent();

				if (grContext == null)
				{
					var glInterface = GRGlInterface.Create();
					grContext = GRContext.CreateGl(glInterface);
				}

				var newSize = new SKSizeI(Width, Height);

				if (renderTarget == null || lastSize != newSize || !renderTarget.IsValid)
				{
					lastSize = newSize;
					GL.GetInteger(GetPName.FramebufferBinding, out var framebuffer);
					GL.GetInteger(GetPName.StencilBits, out var stencil);
					GL.GetInteger(GetPName.Samples, out var samples);
					var maxSamples = grContext.GetMaxSurfaceSampleCount(colorType);
					if (samples > maxSamples)
						samples = maxSamples;
					glInfo = new GRGlFramebufferInfo((uint)framebuffer, colorType.ToGlSizedFormat());

					surface?.Dispose();
					surface = null;
					canvas = null;

					renderTarget?.Dispose();
					renderTarget = new GRBackendRenderTarget(newSize.Width, newSize.Height, samples, stencil, glInfo);
				}

				if (surface == null)
				{
					surface = SKSurface.Create(grContext, renderTarget, surfaceOrigin, colorType, SKColorSpace.CreateSrgb());
					canvas = surface.Canvas;
				}

				using (new SKAutoCanvasRestore(canvas, true))
				{
					base.OnPaintSurface(new SKPaintGLSurfaceEventArgs(surface, renderTarget, surfaceOrigin, colorType));
				}

				canvas.Flush();
				SwapBuffers();

				failureCount = 0;
			}
			catch (Exception)
			{
				failureCount++;
				if (failureCount >= MaxFailures && UseOpenGL)
				{
					UseOpenGL = false;
					OpenGLFailed?.Invoke();
				}
			}
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			canvas = null;
			surface?.Dispose();
			surface = null;
			renderTarget?.Dispose();
			renderTarget = null;
			grContext?.Dispose();
			grContext = null;
		}

		public static Control CreateInstance()
		{
			if (UseOpenGL)
				return new EraPictureBox();
			return new EraSKControl();
		}
	}

	internal sealed class EraSKControl : SKControl
	{
		public EraSKControl()
		{
			SetStyle(ControlStyles.Opaque, true);
		}
	}
}
