using System;

namespace MinorShift.Emuera.Runtime.Utils
{
	/// <summary>
	/// 音频播放基类。平台子类（NAudioSound / AndroidSound）override 虚方法。
	/// </summary>
	internal class Sound
	{
		public volatile bool Playing = false;

		/// <summary>
		/// 平台工厂：由宿主程序设置，用于 Runtime 中动态创建 Sound 实例。
		/// WinForms 设为 () => new NAudioSound()，Android 设为 () => new AndroidSound()。
		/// </summary>
		public static Func<Sound> Factory { get; set; } = () => new Sound();

		public virtual void play(string filename, int repeat = 1) { }
		public virtual void stop() { Playing = false; }
		public virtual void pause() { }
		public virtual void resume() { }
		public virtual void close() { stop(); }
		public virtual bool isPlaying() => Playing;
		public virtual void setVolume(int volume) { }
		public virtual int getVolume() => 0;
		public virtual void setSpeed(float speed) { }
		public virtual double getSpeed() => 1.0;
		public virtual double GetTotalTime() => 0;
		public virtual double GetCurrentTime() => 0;
		public virtual void SetPreservePitch(bool preserve) { }
	}
}
