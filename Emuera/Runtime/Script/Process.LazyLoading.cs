using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Loader;
using MinorShift.Emuera.Runtime.Script.Statements;
using trsl = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.SystemLine;

namespace MinorShift.Emuera.GameProc;

internal sealed partial class Process
{
	//Main Working Sets
	private Dictionary<string, List<string>> LazyLoadingTable { get; } = Config.IgnoreCase ? new(StringComparer.OrdinalIgnoreCase) : new();
	private Dictionary<string, long> LazyLoadingFilesTable { get; } = Config.IgnoreCase ? new(StringComparer.OrdinalIgnoreCase) : new();
	public HashSet<string> LazyLoadingFiles { get; } = new();

	//For changes in files
	public readonly HashSet<string> DeletedFiles = new();
	public readonly HashSet<string> ChangedFiles = new();

	//Paths (修改扩展名为 .bin 以区分旧的明文格式)
	static readonly string LazyLoadingDataFilePath = Path.Join(Program.WorkingDir, "lazyloading.bin");
	static readonly string LazyLoadingFilesFilePath = Path.Join(Program.WorkingDir, "lazyloadingfiles.bin");
	static readonly string LazyLoadingConfigFilePath = Path.Join(Program.WorkingDir, "lazyloading.cfg");
	
	// 版本魔数，用于防止读取旧版或损坏的文件
	private const uint LAZY_MAGIC_NUMBER = 0x4C415A59; // "LAZY"
	private const uint LAZY_VERSION = 1;
	
	public enum LazyStatus
	{
		Disabled,
		NoLazy,
		BuildTable,
		Loaded,
		Error,
		UpdateTable,
	}

	public LazyStatus LazyCurrentLazyStatus = LazyStatus.Disabled;

	public bool TryLazyLoadErb(string functionName)
	{
		if (!LazyLoadingTable.TryGetValue(functionName, out List<string> value))
		{
			return false;
		}

		var loader = new ErbLoader(console, exm, this);
		if (loader.LoadErbList(value, labelDic).GetAwaiter().GetResult())
		{
			if (Program.AnalysisMode)
			{
				foreach (var str in value)
					console.PrintSystemLine(string.Format(trsl.LazyLoadingDebugLoadingFile.Text, str));
			}

			//For updating the table i need this
			//LazyLoadingTable.Remove(functionName); // 로딩이 끝나면 해당 테이블 값은 필요가 없음.
			return true;
		}

		console.PrintSystemLine(string.Format(trsl.LazyLoadingErbFileNotFound.Text, functionName));
		return false;
	}

	private List<string> LoadLazyLoadingFolders()
	{
		char newChar = Path.DirectorySeparatorChar;

		if (!File.Exists(LazyLoadingConfigFilePath))
		{
			console.PrintSystemLine(trsl.LazyLoadingNoConfigFile.Text);
			return null; // 설정파일이 없으므로 일반 풀로딩을 해야 함.
		}

		List<string> ret = new();
		string line;
		try
		{
			using var reader = new StreamReader(LazyLoadingConfigFilePath, Encoding.UTF8);
			while ((line = reader.ReadLine()) != null)
			{
				// 将两种分隔符都替换为当前平台的分隔符
				// lazyloading.cfg 可能在 Windows 上创建（使用 \），但在 Android/Linux 上运行（使用 /）
				var normalized = line.Trim().Replace('\\', newChar).Replace('/', newChar);
				ret.Add(normalized);
			}
		}
		catch (Exception e)
		{
			console.PrintSystemLine(string.Format(trsl.LazyLoadingConfigError.Text, e.Message));
			return null;
		}

		return ret;
	}


	public void LoadLazyLoadingTable(List<KeyValuePair<string, string>> erbFiles)
	{
		if (!File.Exists(LazyLoadingConfigFilePath))
			return;
		if (!File.Exists(LazyLoadingDataFilePath) || !File.Exists(LazyLoadingFilesFilePath))
		{
			LazyCurrentLazyStatus = LazyStatus.BuildTable;
			return; // 설정파일이 없으므로 일반 풀로딩을 해야 함.
		}
		
		try
		{
			var files = GetLazyFiles(erbFiles);

			// =================================================================
			// 1. 读取文件元数据 (lazyloadingfiles.bin)
			// =================================================================
			using (var fsMeta = new FileStream(LazyLoadingFilesFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
			using (var metaReader = new BinaryReader(fsMeta, Encoding.UTF8))
			{
				// 校验魔数和版本
				if (metaReader.ReadUInt32() != LAZY_MAGIC_NUMBER || metaReader.ReadUInt32() != LAZY_VERSION)
				{
					LazyCurrentLazyStatus = LazyStatus.BuildTable;
					return;
				}

				int fileCount = metaReader.ReadInt32();
				for (int i = 0; i < fileCount; i++)
				{
					string name = metaReader.ReadString();
					long fLastWrite = metaReader.ReadInt64();
					
					var path = ErbPath(name);

					if (File.Exists(path))
					{
						if (File.GetLastWriteTime(path).ToFileTimeUtc() != fLastWrite)
						{
							ChangedFiles.Add(name);
						}
						else
						{
							LazyLoadingFilesTable.Add(name, fLastWrite);
						}
					}
					else
					{
						DeletedFiles.Add(name);
					}
				}
			}

			files.ExceptWith(LazyLoadingFilesTable.Keys);
			ChangedFiles.UnionWith(files);

			// =================================================================
			// 2. 读取函数映射数据 (lazyloading.bin)
			// =================================================================
			using (var fsData = new FileStream(LazyLoadingDataFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
			using (var dataReader = new BinaryReader(fsData, Encoding.UTF8))
			{
				// 校验魔数和版本
				if (dataReader.ReadUInt32() != LAZY_MAGIC_NUMBER || dataReader.ReadUInt32() != LAZY_VERSION)
				{
					LazyCurrentLazyStatus = LazyStatus.BuildTable;
					return;
				}

				int funcCount = dataReader.ReadInt32();
				for (int i = 0; i < funcCount; i++)
				{
					string funcName = dataReader.ReadString();
					string fileName = dataReader.ReadString();

					if (ChangedFiles.Contains(fileName) || DeletedFiles.Contains(fileName))
						continue;

					if (!LazyLoadingTable.ContainsKey(funcName))
						LazyLoadingTable.Add(funcName, new List<string>());

					string path = Program.ErbDir + fileName;
					if (!LazyLoadingFiles.Add(path))
						path = LazyLoadingFiles.First(x => x == path);
					LazyLoadingTable[funcName].Add(path);
				}
			}
		}
		catch (Exception e)
		{
			console.PrintSystemLine(string.Format(trsl.LazyLoadingTableReadError.Text, e.Message));
			// 如果读取二进制文件失败（可能是旧版明文文件残留），强制重新构建
			LazyCurrentLazyStatus = LazyStatus.BuildTable; 
			return;
		}
		LazyCurrentLazyStatus = ChangedFiles.Count != 0 || DeletedFiles.Count != 0 ? LazyStatus.UpdateTable : LazyStatus.Loaded;
	}

	private HashSet<string> GetLazyFiles(IEnumerable<KeyValuePair<string, string>> erbFiles)
	{
		var paths = LoadLazyLoadingFolders();
		if (paths == null)
			return new HashSet<string>();

		var ret = from pair in erbFiles
			from path in paths
			where pair.Key.StartsWith(path) 
			select pair.Key;
		HashSet<string> files = new(ret);
		return files;
	}

	public void SaveLazyLoadingList(List<FunctionLabelLine> labels, List<KeyValuePair<string, string>> erbFiles)
	{
		HashSet<string> files = GetLazyFiles(erbFiles);

		if (files.Count == 0)
		{
			LazyCurrentLazyStatus = LazyStatus.NoLazy;
			return;
		}

		foreach (FunctionLabelLine label in labels)
		{
			if (!files.Contains(label.Position.Value.Filename))
				continue;

			if (label.IsMethod)
			{
				if (Program.AnalysisMode)
					console.PrintSystemLine(string.Format(trsl.LazyLoadingFileFunctionExcluded.Text,
						label.Position.Value.Filename, label.LabelName));
				files.Remove(label.Position.Value.Filename);
				continue;
			}

			if (label.IsEvent)
			{
				console.PrintSystemLine(string.Format(trsl.LazyLoadingFileEventExcluded.Text, label.Position.Value.Filename,
					label.LabelName));
				files.Remove(label.Position.Value.Filename);
			}
		}

		try
		{
			// =================================================================
			// 1. 收集需要写入的数据
			// =================================================================
			var validLabels = new List<FunctionLabelLine>();
			var metafiles = new HashSet<string>();
			
			foreach (FunctionLabelLine label in labels)
			{
				if (files.Contains(label.Position.Value.Filename))
				{
					validLabels.Add(label);
					metafiles.Add(label.Position.Value.Filename);
				}
			}

			// =================================================================
			// 2. 写入函数映射数据 (lazyloading.bin)
			// =================================================================
			using (var fsData = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
			using (var dataWriter = new BinaryWriter(fsData, Encoding.UTF8))
			{
				dataWriter.Write(LAZY_MAGIC_NUMBER);
				dataWriter.Write(LAZY_VERSION);
				dataWriter.Write(validLabels.Count); // 写入总数，方便读取时预分配内存

				foreach (var label in validLabels)
				{
					dataWriter.Write(label.LabelName);
					dataWriter.Write(label.Position.Value.Filename);
				}
			}

			// =================================================================
			// 3. 写入文件元数据 (lazyloadingfiles.bin)
			// =================================================================
			using (var fsMeta = new FileStream(LazyLoadingFilesFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
			using (var metaWriter = new BinaryWriter(fsMeta, Encoding.UTF8))
			{
				metaWriter.Write(LAZY_MAGIC_NUMBER);
				metaWriter.Write(LAZY_VERSION);
				metaWriter.Write(metafiles.Count);

				foreach (var name in metafiles)
				{
					var lastWrite = File.GetLastWriteTime(ErbPath(name)).ToFileTimeUtc();
					metaWriter.Write(name);
					metaWriter.Write(lastWrite);
				}
			}
		}
		catch (Exception e)
		{
			console.PrintSystemLine(string.Format(trsl.LazyLoadingTableSaveError.Text, e.Message));
			LazyCurrentLazyStatus = LazyStatus.Error;
		}
	}

	public bool SavePartialLazyLoadingList(List<FunctionLabelLine> labels)
	{
		var temp_labels = new List<FunctionLabelLine>();
		var validLabelsToAppend = new List<FunctionLabelLine>();
		var labelFiles = new HashSet<string>();
		
		foreach (var file in ChangedFiles.ToList())
		{
			var valid = true;
			foreach (FunctionLabelLine label in labels)
			{
				if(label.Position.Value.Filename != file)
					continue;
				
				if (label.IsMethod)
				{
					if (Program.AnalysisMode)
						console.PrintSystemLine(string.Format(trsl.LazyLoadingFileFunctionExcluded.Text,
							label.Position.Value.Filename, label.LabelName));
					valid = false;
					break;
				}

				if (label.IsEvent)
				{
					if (Program.AnalysisMode)
						console.PrintSystemLine(string.Format(trsl.LazyLoadingFileEventExcluded.Text,
							label.Position.Value.Filename, label.LabelName));
					valid = false;
					break;
				}
				temp_labels.Add(label);
			}

			if (!valid || temp_labels.Count == 0)
			{
				ChangedFiles.Remove(file);
			}
			else
			{
				foreach (var label in temp_labels)
				{
					validLabelsToAppend.Add(label);
					labelFiles.Add(label.Position.Value.Filename);
				}
			}
			temp_labels.Clear();
		}

		if (ChangedFiles.Count == 0 && DeletedFiles.Count == 0)
		{
			LazyCurrentLazyStatus = LazyStatus.Loaded;
			return false;
		}
		
		if(ChangedFiles.Count != 0)
			console.PrintSystemLine(trsl.LazyLoadingFilesModified.Text + ChangedFiles.Count);
		if(DeletedFiles.Count != 0)
			console.PrintSystemLine(trsl.LazyLoadingFilesDeleted.Text + DeletedFiles.Count);
		
		try
		{
			// =================================================================
			// 1. 更新函数映射数据 (lazyloading.bin)
			// =================================================================
			using (var fsData = new FileStream(LazyLoadingDataFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
			using (var dataWriter = new BinaryWriter(fsData, Encoding.UTF8))
			{
				dataWriter.Write(LAZY_MAGIC_NUMBER);
				dataWriter.Write(LAZY_VERSION);
				
				// 计算总数: 新增/修改的标签数 + 内存中未改变的标签数
				int totalFuncs = validLabelsToAppend.Count + LazyLoadingTable.Sum(x => x.Value.Count);
				dataWriter.Write(totalFuncs);

				// 写入新增/修改的
				foreach (var label in validLabelsToAppend)
				{
					dataWriter.Write(label.LabelName);
					dataWriter.Write(label.Position.Value.Filename);
				}

				// 写入内存中未改变的
				foreach (var item in LazyLoadingTable)
				{
					// 注意：内存里的路径是完整路径，需要截取掉 Program.ErbDir 部分
					string relPath = item.Value[0][Program.ErbDir.Length..];
					dataWriter.Write(item.Key);
					dataWriter.Write(relPath);
				}
			}

			// =================================================================
			// 2. 更新文件元数据 (lazyloadingfiles.bin)
			// =================================================================
			using (var fsMeta = new FileStream(LazyLoadingFilesFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
			using (var metaWriter = new BinaryWriter(fsMeta, Encoding.UTF8))
			{
				metaWriter.Write(LAZY_MAGIC_NUMBER);
				metaWriter.Write(LAZY_VERSION);
				
				int totalFiles = labelFiles.Count + LazyLoadingFilesTable.Count;
				metaWriter.Write(totalFiles);

				// 写入新增/修改的文件
				foreach (var name in labelFiles)
				{
					var lastWrite = File.GetLastWriteTime(ErbPath(name)).ToFileTimeUtc();
					metaWriter.Write(name);
					metaWriter.Write(lastWrite);
				}

				// 写入内存中未改变的文件
				foreach (var item in LazyLoadingFilesTable)
				{
					metaWriter.Write(item.Key);
					metaWriter.Write(item.Value);
				}
			}
		}
		catch (Exception e)
		{
			console.PrintSystemLine(string.Format(trsl.LazyLoadingTableSaveError.Text, e.Message));
			LazyCurrentLazyStatus = LazyStatus.Error;
			return false;
		}

		LazyCurrentLazyStatus = LazyStatus.Loaded;
		return true;
	}

	static string ErbPath(string a)
	{
		return Program.ErbDir + a;
	}
}