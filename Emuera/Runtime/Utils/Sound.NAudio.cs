using System;
using System.Threading;
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using NAudio.Extras;
using NAudio.Vorbis;
using NAudio.CoreAudioApi.Interfaces;
using VarispeedDemo.SoundTouch;
namespace MinorShift.Emuera.Runtime.Utils
{
	internal class AudioDeviceTracker : IMMNotificationClient
	{
		private readonly SynchronizationContext syncContext;
		private MMDeviceEnumerator enumerator;
		public MMDevice Device { get; private set; }
		public event EventHandler<MMDevice> DefaultDeviceChanged;

		public AudioDeviceTracker()
		{
			if (SynchronizationContext.Current == null)
				throw new Exception("SynchronizationContext.Current is null");

			syncContext = SynchronizationContext.Current;
			enumerator = new MMDeviceEnumerator();

			if (enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console))
				Device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
			else
				Device = null;

			enumerator.RegisterEndpointNotificationCallback(this);
		}

		~AudioDeviceTracker()
		{
			enumerator.UnregisterEndpointNotificationCallback(this);
		}

		public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
		{
			if (flow != DataFlow.Render || role != Role.Console)
				return;

			syncContext.Post(DispatchDefaultDeviceChanged, defaultDeviceId);
		}

		private void DispatchDefaultDeviceChanged(object defaultDeviceId)
		{
			if (Device?.ID != (string)defaultDeviceId)
			{
				if (defaultDeviceId == null)
					Device = null;
				else
					Device = enumerator.GetDevice((string)defaultDeviceId);

				var handler = DefaultDeviceChanged;
				if (handler != null)
				{
					handler(this, Device);
				}
			}
		}

		public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
		public void OnDeviceAdded(string pwstrDeviceId) { }
		public void OnDeviceRemoved(string deviceId) { }
		public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
	}

	internal class DummyOut : IWavePlayer
	{
		private readonly SynchronizationContext syncContext;
		private volatile PlaybackState playbackState = PlaybackState.Stopped;
		private Thread playThread;
		private ISampleProvider sampleProvider;
		private int latency;
		public float Volume { get; set; }
		public PlaybackState PlaybackState { get => playbackState; }
		public WaveFormat OutputWaveFormat { get; }
		public event EventHandler<StoppedEventArgs> PlaybackStopped;

		public DummyOut(int latency)
		{
			syncContext = SynchronizationContext.Current;
			this.latency = latency;
		}

		public void Play()
		{
			if (playbackState != PlaybackState.Playing)
			{
				if (playbackState == PlaybackState.Stopped)
				{
					playThread = new Thread(PlayThread);
					playThread.IsBackground = true;
					playbackState = PlaybackState.Playing;
					playThread.Start();
				}
				else
				{
					playbackState = PlaybackState.Playing;
				}
			}
		}

		public void Stop()
		{
			if (playbackState != PlaybackState.Stopped)
			{
				playbackState = PlaybackState.Stopped;
				playThread.Join();
				playThread = null;
			}
		}
		public void Pause()
		{
			if (playbackState == PlaybackState.Playing)
				playbackState = PlaybackState.Paused;
		}
		public void Init(IWaveProvider waveProvider)
		{
			sampleProvider = waveProvider.ToSampleProvider();
		}
		public void Dispose()
		{
			Stop();
		}

		private void PlayThread()
		{
			Exception exception = null;
			try
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				TimeSpan prevTime = TimeSpan.Zero;
				var format = sampleProvider.WaveFormat;
				float[] buffer = new float[format.SampleRate * format.Channels];
				int samplesRemaining = 0;

				while (playbackState != PlaybackState.Stopped)
				{
					Thread.Sleep(latency);
					if (playbackState == PlaybackState.Playing)
					{
						if (!stopwatch.IsRunning)
						{
							stopwatch.Start();
							continue;
						}

						TimeSpan now = stopwatch.Elapsed;
						TimeSpan delta = now - prevTime;
						prevTime = now;

						samplesRemaining += (int)(delta.TotalSeconds * format.SampleRate * format.Channels);
						while (samplesRemaining > format.Channels)
						{
							int count = Math.Min(buffer.Length, samplesRemaining);
							count -= count % format.Channels;
							int samplesRead = sampleProvider.Read(buffer, 0, count);
							samplesRemaining -= samplesRead;
							if (samplesRead == 0)
							{
								playbackState = PlaybackState.Stopped;
								break;
							}
						}
					}
					else if (playbackState == PlaybackState.Paused)
					{
						if (stopwatch.IsRunning)
						{
							stopwatch.Reset();
							prevTime = TimeSpan.Zero;
						}
					}
				}
			}
			catch (Exception e)
			{
				exception = e;
			}
			finally
			{
				var handler = PlaybackStopped;
				if (handler != null)
				{
					if (syncContext == null)
						handler(this, new StoppedEventArgs(exception));
					else
						syncContext.Post(state => handler(this, new StoppedEventArgs(exception)), null);
				}
			}
		}
	}

	internal class RepeatStream : WaveStream
	{
		readonly WaveStream sourceStream;
		readonly int total_count;
		int remaining_count;

		public RepeatStream(WaveStream source, int count)
		{
			sourceStream = source;
			total_count = count;
			remaining_count = count;
		}

		public override WaveFormat WaveFormat
		{
			get { return sourceStream.WaveFormat; }
		}

		public override long Length
		{
			get { return sourceStream.Length * total_count; }
		}

		public override long Position
		{
			get
			{
				return (total_count - remaining_count) * sourceStream.Length + sourceStream.Position;
			}
			set
			{
				remaining_count = (int)(value / sourceStream.Length);
				sourceStream.Position = value % sourceStream.Length;
			}
		}

		public override bool HasData(int count)
		{
			return sourceStream.Position < sourceStream.Length;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			int total_read = 0;
			while (total_read < count)
			{
				int remaining = count - total_read;
				int read = sourceStream.Read(buffer, offset + total_read, remaining);
				if (read < remaining || sourceStream.Position >= sourceStream.Length)
				{
					if (remaining_count > 1)
					{
						remaining_count--;
						sourceStream.Position = 0;
					}
					else
					{
						return total_read + read;
					}
				}
				total_read += read;
			}
			return total_read;
		}

		protected override void Dispose(bool disposing)
		{
			sourceStream.Dispose();
			base.Dispose(disposing);
		}
	}

	internal static class SoundMixer
	{
		private static bool initialized = false;
		public static bool Initialized { get => initialized; }
		public const int SampleRate = 44100;
		private static AudioDeviceTracker deviceTracker;
		private static IWavePlayer output;
		private static MixingSampleProvider mixer;

		public static void Initialize()
		{
			if (initialized)
				return;

			mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2));
			mixer.ReadFully = true;
			mixer.MixerInputEnded += SoundEnded;

			deviceTracker = new AudioDeviceTracker();
			InitializeOutput(deviceTracker.Device);
			deviceTracker.DefaultDeviceChanged += ChangeOutput;

			initialized = true;
		}

		private static void SoundEnded(object sender, SampleProviderEventArgs args)
		{
			((NAudioSound)(args.SampleProvider)).Playing = false;
		}

		private static void InitializeOutput(MMDevice device)
		{
			if (SynchronizationContext.Current == null)
				throw new Exception("SynchronizationContext.Current is null");

			if (device != null)
				output = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);
			else
				output = new DummyOut(200);

			output.Init(mixer);
			output.Play();
		}

		private static void ChangeOutput(object sender, MMDevice device)
		{
			if (output != null)
				output.Dispose();

			InitializeOutput(device);
		}

		public static void PlaySound(NAudioSound sound)
		{
			sound.Playing = true;
			mixer.AddMixerInput(sound);
		}

		public static void StopSound(NAudioSound sound)
		{
			sound.Playing = false;
			mixer.RemoveMixerInput(sound);
		}
	}

	internal class NAudioSound : Sound, ISampleProvider
	{
		private float volume = 1.0f;
		private bool paused = false;  // 新增：暂停状态标志
		private long savedPosition = 0; // 新增：保存暂停时的位置
		private VarispeedSampleProvider varispeedProvider; // 新增：用于控制播放速度的 VarispeedSampleProvider
		private bool preservePitch = true; // 新增：默认保持音调不变
		private WaveStream stream; // 确保这个字段保留对原始流的引用
		private VolumeSampleProvider volumeProvider;
		public WaveFormat WaveFormat { get => volumeProvider.WaveFormat; }
	
		// 新增：获取当前播放时间（秒）
		public override double GetCurrentTime()
		{
			if (stream == null || volumeProvider == null) return 0;
			
			// 注意：这里读取的是原始流的位置。
			// 如果使用了 WdlResamplingSampleProvider，原始流的位置和实际播放位置会有偏差（重采样导致采样点数变化）。
			// 但对于简单的进度显示，这通常是可以接受的近似值。
			
			// 计算公式：字节位置 / (采样率 * 声道数 * 每个采样的字节数)
			// 假设最终输出是 IEEE Float (32bit)，即 4 字节
			long bytesPerSecond = stream.WaveFormat.SampleRate * stream.WaveFormat.Channels * 4;
			if (bytesPerSecond == 0) return 0;
			
			return (double)stream.Position / bytesPerSecond;
		}

		// 新增：获取音频总长度（秒）
		public override double GetTotalTime()
		{
			if (stream == null || volumeProvider == null) return 0;
			
			long bytesPerSecond = stream.WaveFormat.SampleRate * stream.WaveFormat.Channels * 4;
			if (bytesPerSecond == 0) return 0;
			
			return (double)stream.Length / bytesPerSecond;
		}

		public int Read(float[] buffer, int offset, int count)
		{
			return volumeProvider.Read(buffer, offset, count);
		}

		public override void play(string filename, int repeat = 1)
		{
			if (!SoundMixer.Initialized)
				SoundMixer.Initialize();

			stop();

			if (filename.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
			{
				stream = new WaveFileReader(filename);
			}
			else if (filename.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
			{
				stream = new VorbisWaveReader(filename);
			}
			else
			{
				// NOTE: MediaFoundationReader currently seems to only support 16 bit audio files on wine
				var settings = new MediaFoundationReader.MediaFoundationReaderSettings();
				settings.RequestFloatOutput = true;
				stream = new MediaFoundationReader(filename, settings);
			}

			WaveStream _stream = stream;
			// LoopStream / RepeatStream might cause issues because they seek (see comment in stop method below)
			if (repeat == -1)
				_stream = new LoopStream(_stream);
			else if (repeat > 1)
				_stream = new RepeatStream(_stream, repeat);

			// MediaFoundationResampler is faster and possibly higher quality than WdlResamplingSampleProvider but currently doesn't seem to work on wine
			// var resampler = new MediaFoundationResampler(_stream, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
			// volumeProvider = new VolumeSampleProvider(resampler.ToSampleProvider());

			ISampleProvider sampleProvider = _stream.ToSampleProvider();
			// 1. 先进行重采样
			if (sampleProvider.WaveFormat.SampleRate != SoundMixer.SampleRate)
				sampleProvider = new WdlResamplingSampleProvider(sampleProvider, SoundMixer.SampleRate);
			// 2. 单声道转立体声
			if (sampleProvider.WaveFormat.Channels == 1)
				sampleProvider = sampleProvider.ToStereo();
			// 3. 添加变速处理（在重采样之后）
			varispeedProvider = new VarispeedSampleProvider(sampleProvider, 100, 
				new SoundTouchProfile(preservePitch, true)); // 使用可配置的 preservePitch，默认为 true，使用抗锯齿
			// 4. 最后添加音量控制
			volumeProvider = new VolumeSampleProvider(varispeedProvider);
			volumeProvider.Volume = volume;

			SoundMixer.PlaySound(this);
		}
		// 新增：暂停方法
		public override void pause()
		{
			if (!SoundMixer.Initialized || !Playing)
				return;
				 // 保存当前流位置
			if (stream != null)
				savedPosition = stream.Position;
			
    		// 清除 SoundTouch 缓冲区
			if (varispeedProvider != null)
				varispeedProvider.Reposition(); // 保存当前播放位置
			paused = true;
			SoundMixer.StopSound(this);  // 从混音器中移除
		}
		// 新增：恢复方法
		public override void resume()
		{
			if (!SoundMixer.Initialized || !paused)
				return;
			
			paused = false;
			// 恢复位置
			if (stream != null && savedPosition > 0)
			{
				stream.Position = savedPosition;
			}
			SoundMixer.PlaySound(this);  // 重新添加到混音器
		}
		public override void stop()
		{
			if (SoundMixer.Initialized)
			{
			    SoundMixer.StopSound(this);
			}
			paused = false;  // 重置暂停状态
			// don't try to reuse the stream because repositioning a MediaFoundationReader to the beginning sometimes causes WasapiOut to hang when the stream is next read (observed with a 48khz 24 bit FLAC file)
			if (stream != null)
			{
				stream.Dispose();
				stream = null;
			}
		}

		public override void close()
		{
			stop();
			volumeProvider = null;
		}

		public override bool isPlaying()
		{
			return Playing && !paused;
		}

		public override void setVolume(int volume)
		{
			this.volume = Math.Clamp(volume, 0, 100) / 100.0f;
			if (volumeProvider != null)
				volumeProvider.Volume = this.volume;
		}
		public override int getVolume()
		{
			return (int)(volume * 100);
		}
		// 添加变速方法
		public override void setSpeed(float speed)
		{
			if (varispeedProvider != null)
			{
				// 限制变速范围在 0.1x 到 10x 之间
				float clampedSpeed = Math.Clamp(speed, 0.1f, 10.0f);
				varispeedProvider.PlaybackRate = clampedSpeed;
			}
		}

		public override double getSpeed()
		{
			return varispeedProvider?.PlaybackRate ?? 1.0;
		}
		/// <summary>
		/// 跳转到指定时间位置（秒）
		/// </summary>
		public void Seek(double timeInSeconds)
		{
			if (stream == null || volumeProvider == null)
				return;
			
			// 计算目标字节位置
			long bytesPerSecond = stream.WaveFormat.SampleRate * stream.WaveFormat.Channels * 4;
			long targetPosition = (long)(timeInSeconds * bytesPerSecond);
			
			// 确保位置在有效范围内
			targetPosition = Math.Clamp(targetPosition, 0, stream.Length);
			
			// 设置流位置
			stream.Position = targetPosition;
			
			// 清除 SoundTouch 缓冲区
			if (varispeedProvider != null)
				varispeedProvider.Reposition();
		}
		/// <summary>
		/// 相对当前位置跳转（秒）
		/// </summary>
		public void SeekRelative(double offsetInSeconds)
		{
			if (stream == null || volumeProvider == null)
				return;
			
			// 计算当前时间
			double currentTime = GetCurrentTime();
			
			// 计算目标时间
			double targetTime = currentTime + offsetInSeconds;
			
			// 调用绝对跳转
			Seek(targetTime);
		}
		/// <summary>
		/// 跳转到指定百分比位置（0.0-1.0）
		/// </summary>
		public void SeekPercentage(double percentage)
		{
			if (stream == null || volumeProvider == null)
				return;
			
			// 确保百分比在有效范围内
			percentage = Math.Clamp(percentage, 0.0, 1.0);
			
			// 计算目标时间
			double totalTime = GetTotalTime();
			double targetTime = totalTime * percentage;
			
			// 调用绝对跳转
			Seek(targetTime);
		}
		// 添加设置音调保持的方法
		public override void SetPreservePitch(bool preserve)
		{
			preservePitch = preserve;
			if (varispeedProvider != null)
			{
				varispeedProvider.SetSoundTouchProfile(new SoundTouchProfile(preserve, true));
			}
		}
	}
}