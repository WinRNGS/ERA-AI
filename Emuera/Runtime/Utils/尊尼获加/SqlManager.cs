using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Data.Sqlite;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;

namespace MinorShift.Emuera.GameData.Function
{
	internal static partial class SqlManager
	{
		private static Dictionary<string, SqliteConnection> _connections = new Dictionary<string, SqliteConnection>(StringComparer.OrdinalIgnoreCase);
		
		public static bool ConnectionOpen(string name)
		{
			if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
				throw new CodeEE($"SQL_CONNECTION_OPEN: 无效或不安全的数据库名称 '{name}'。名称不能包含路径字符或非法字符。");

			string dir = Config.SavDir + "sql" + Path.DirectorySeparatorChar;
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			if (_connections.TryGetValue(name, out var existingConn))
			{
				existingConn.Close();
				existingConn.Dispose();
				_connections.Remove(name);
			}

			var connStr = new SqliteConnectionStringBuilder
			{
				DataSource = Path.Combine(dir, $"{name}.db")
			};
			var conn = new SqliteConnection(connStr.ConnectionString);
			try
			{
				conn.Open();

				using (var cmd = conn.CreateCommand())
				{
					cmd.CommandText = "PRAGMA journal_mode = WAL;PRAGMA synchronous = NORMAL;";
					cmd.ExecuteNonQuery();
				}

				_connections[name] = conn;
				return true;
			}
			catch (Exception ex)
			{
				conn.Dispose();
				throw new CodeEE($"SQL_CONNECTION_OPEN 失败: {ex.Message}");
			}
		}
		
		public class ReaderContext
		{
			public SqliteCommand Command;
			public SqliteDataReader Reader;
		}
		
		private static Dictionary<long, ReaderContext> _readers = new Dictionary<long, ReaderContext>();
		private static long _nextReaderId = 1;

		// 1. 连接数据库 (支持 :memory: 内存数据库)
		public static bool Connect(string dbName, string connectionString)
		{
			if (_connections.ContainsKey(dbName)) return true; // 已连接
			
			try
			{
				var conn = new SqliteConnection(connectionString);
				conn.Open();
				_connections[dbName] = conn;
				return true;
			}
			catch (Exception ex)
			{
				throw new CodeEE($"SQL_CONNECT 失败: {ex.Message}");
			}
		}

		// 2. 断开连接
		public static void Disconnect(string dbName)
		{
			if (_connections.TryGetValue(dbName, out var conn))
			{
				conn.Close();
				conn.Dispose();
				_connections.Remove(dbName);
			}
		}

		// 3. 执行非查询语句
		public static long ExecuteNonQuery(string dbName, string sql, string[] paramValues = null)
		{
			if (!_connections.TryGetValue(dbName, out var conn))
				throw new CodeEE($"数据库 '{dbName}' 未连接。");

			try
			{
				using var cmd = conn.CreateCommand();
				cmd.CommandText = sql;
				if (paramValues != null)
				{
					for (int i = 0; i < paramValues.Length; i++)
					{
						var p = cmd.CreateParameter();
						p.ParameterName = "@" + i;
						p.Value = paramValues[i] ?? (object)DBNull.Value;
						cmd.Parameters.Add(p);
					}
				}
				return cmd.ExecuteNonQuery();
			}
			catch (Exception ex)
			{
				throw new CodeEE($"SQL 执行错误: {ex.Message}\n语句: {sql}");
			}
		}

		// 4. 执行查询语句 (SELECT)
		public static long ExecuteReader(string dbName, string sql, string[] paramValues = null)
		{
			if (!_connections.TryGetValue(dbName, out var conn))
				throw new CodeEE($"数据库 '{dbName}' 未连接。");

			try
			{
				var cmd = conn.CreateCommand();
				cmd.CommandText = sql;
				if (paramValues != null)
				{
					for (int i = 0; i < paramValues.Length; i++)
					{
						var p = cmd.CreateParameter();
						p.ParameterName = "@" + i;
						p.Value = paramValues[i] ?? (object)DBNull.Value;
						cmd.Parameters.Add(p);
					}
				}
				var reader = cmd.ExecuteReader();

				long id = _nextReaderId++;
				_readers[id] = new ReaderContext { Command = cmd, Reader = reader };
				return id;
			}
			catch (Exception ex)
			{
				throw new CodeEE($"SQL 查询错误: {ex.Message}\n语句: {sql}");
			}
		}

		// 5. 读取下一行
		public static long ReaderRead(long readerId)
		{
			if (_readers.TryGetValue(readerId, out var ctx))
			{
				return ctx.Reader.Read() ? 1 : 0;
			}
			return 0;
		}

		// 6. 获取整数列
		public static long ReaderGetLong(long readerId, int columnIndex)
		{
			if (_readers.TryGetValue(readerId, out var ctx))
			{
				if (ctx.Reader.IsDBNull(columnIndex)) return 0;
				return ctx.Reader.GetInt64(columnIndex);
			}
			throw new CodeEE($"无效的 Reader ID: {readerId}");
		}

		// 7. 获取字符串列
		public static string ReaderGetString(long readerId, int columnIndex)
		{
			if (_readers.TryGetValue(readerId, out var ctx))
			{
				if (ctx.Reader.IsDBNull(columnIndex)) return string.Empty;
				return ctx.Reader.GetString(columnIndex);
			}
			throw new CodeEE($"无效的 Reader ID: {readerId}");
		}

		// 7b. 获取浮点数列
		public static double ReaderGetFloat(long readerId, int columnIndex)
		{
			if (_readers.TryGetValue(readerId, out var ctx))
			{
				if (ctx.Reader.IsDBNull(columnIndex)) return 0.0;
				return ctx.Reader.GetDouble(columnIndex);
			}
			throw new CodeEE($"无效的 Reader ID: {readerId}");
		}

		// 8. 检查是否为 NULL
		public static long ReaderIsNull(long readerId, int columnIndex)
		{
			if (_readers.TryGetValue(readerId, out var ctx))
			{
				return ctx.Reader.IsDBNull(columnIndex) ? 1 : 0;
			}
			throw new CodeEE($"无效的 Reader ID: {readerId}");
		}

		// 9. 关闭 Reader
		public static void ReaderClose(long readerId)
		{
			if (_readers.TryGetValue(readerId, out var ctx))
			{
				ctx.Reader.Close();
				ctx.Reader.Dispose();
				ctx.Command.Dispose();
				_readers.Remove(readerId);
			}
		}

		// 清理所有资源
		public static void CloseAll()
		{
			foreach (var ctx in _readers.Values)
			{
				ctx.Reader?.Dispose();
				ctx.Command?.Dispose();
			}
			_readers.Clear();

			foreach (var conn in _connections.Values)
			{
				conn?.Dispose();
			}
			_connections.Clear();
		}

		private static T ExecuteScalar<T>(string dbName, string sql, string[] paramValues = null)
		{
			if (!_connections.TryGetValue(dbName, out var conn))
				throw new CodeEE($"数据库 '{dbName}' 未连接。");

			try
			{
				using var cmd = conn.CreateCommand();
				cmd.CommandText = sql;
				if (paramValues != null)
				{
					for (int i = 0; i < paramValues.Length; i++)
					{
						var p = cmd.CreateParameter();
						p.ParameterName = "@" + i;
						p.Value = paramValues[i] ?? (object)DBNull.Value;
						cmd.Parameters.Add(p);
					}
				}
				var result = cmd.ExecuteScalar();
				if (result == null || result == DBNull.Value)
				{
					if (typeof(T) == typeof(long)) return (T)(object)0L;
					if (typeof(T) == typeof(double)) return (T)(object)0.0;
					if (typeof(T) == typeof(string)) return (T)(object)string.Empty;
					return default;
				}
				if (typeof(T) == typeof(long)) return (T)(object)Convert.ToInt64(result);
				if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(result);
				return (T)(object)result.ToString();
			}
			catch (Exception ex)
			{
				throw new CodeEE($"SQL 标量查询错误: {ex.Message}\n语句: {sql}");
			}
		}

		public static long ExecuteScalarLong(string dbName, string sql, string[] paramValues = null)
			=> ExecuteScalar<long>(dbName, sql, paramValues);

		public static string ExecuteScalarString(string dbName, string sql, string[] paramValues = null)
			=> ExecuteScalar<string>(dbName, sql, paramValues);

		public static double ExecuteScalarFloat(string dbName, string sql, string[] paramValues = null)
			=> ExecuteScalar<double>(dbName, sql, paramValues);

        // 12. [流式导入] 将 MAP 格式的 XML 直接导入到 SQLite
        public static long ImportMapXml(string dbName, string tableName, string filePath)
        {
            if (!_connections.TryGetValue(dbName, out var conn)) return 0;
            
            string fullPath = Path.Combine(Program.WorkingDir, filePath);
            if (!File.Exists(fullPath)) fullPath = Path.Combine(Program.ExeDir, filePath);
            if (!File.Exists(fullPath)) throw new CodeEE($"SQL_IMPORT_MAP_XML: 找不到文件 '{filePath}'");

            try
            {
                // 使用 XmlDocument 确保 100% 兼容各种缩进和换行的 XML
                XmlDocument doc = new XmlDocument();
                doc.Load(fullPath);

                using var trans = conn.BeginTransaction();
                
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} (k TEXT PRIMARY KEY, v TEXT);";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = $"INSERT OR REPLACE INTO {tableName} (k, v) VALUES (@k, @v);";
                    var pK = cmd.Parameters.Add("@k", SqliteType.Text);
                    var pV = cmd.Parameters.Add("@v", SqliteType.Text);

                    XmlNodeList nodes = doc.SelectNodes("/map/p");
                    if (nodes != null)
                    {
                        foreach (XmlNode node in nodes)
                        {
                            var keyNode = node.SelectSingleNode("./k");
                            var valNode = node.SelectSingleNode("./v");
                            
                            if (keyNode != null && valNode != null)
                            {
                                pK.Value = keyNode.InnerText;
                                pV.Value = valNode.InnerXml; // 保持内部 XML 标签(如果有的话)
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }
                trans.Commit();
                return 1;
            }
            catch (Exception ex)
            {
                throw new CodeEE($"导入 MAP XML 失败: {ex.Message}");
            }
        }
        // 13. [流式导入] 将 DT 格式的 XML 导入到 SQLite
        public static long ImportDtXml(string dbName, string tableName, string schemaPath, string dataPath)
        {
            if (!_connections.TryGetValue(dbName, out var conn)) return 0;

            string fullSchema = Path.Combine(Program.WorkingDir, schemaPath);
            if (!File.Exists(fullSchema)) fullSchema = Path.Combine(Program.ExeDir, schemaPath);
            
            string fullData = Path.Combine(Program.WorkingDir, dataPath);
            if (!File.Exists(fullData)) fullData = Path.Combine(Program.ExeDir, dataPath);

            if (!File.Exists(fullSchema)) throw new CodeEE($"SQL_IMPORT_DT_XML: 找不到架构文件 '{schemaPath}'");
            if (!File.Exists(fullData)) throw new CodeEE($"SQL_IMPORT_DT_XML: 找不到数据文件 '{dataPath}'");

            try
            {
                DataTable schemaDt = new DataTable(tableName);
                schemaDt.ReadXmlSchema(fullSchema);

                using var trans = conn.BeginTransaction();

                List<string> columns = new List<string>();
                foreach (DataColumn col in schemaDt.Columns)
                {
                    string type = (col.DataType == typeof(long) || col.DataType == typeof(int)) ? "INTEGER" : "TEXT";
                    string pk = col.ColumnName.ToLower() == "id" ? " PRIMARY KEY" : "";
                    columns.Add($"{col.ColumnName} {type}{pk}");
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} ({string.Join(", ", columns)});";
                    cmd.ExecuteNonQuery();
                }

                string colNames = string.Join(", ", schemaDt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
                string paramNames = string.Join(", ", schemaDt.Columns.Cast<DataColumn>().Select(c => "@" + c.ColumnName));
                
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = trans;
                    cmd.CommandText = $"INSERT OR REPLACE INTO {tableName} ({colNames}) VALUES ({paramNames});";
                    foreach (DataColumn col in schemaDt.Columns)
                        cmd.Parameters.Add("@" + col.ColumnName, (col.DataType == typeof(long) || col.DataType == typeof(int)) ? SqliteType.Integer : SqliteType.Text);

                    using (XmlReader reader = XmlReader.Create(fullData))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Name == tableName)
                            {
                                using (XmlReader rowReader = reader.ReadSubtree())
                                {
                                    foreach (SqliteParameter p in cmd.Parameters) p.Value = DBNull.Value;

                                    while (rowReader.Read())
                                    {
                                        if (rowReader.NodeType == XmlNodeType.Element && rowReader.Name != tableName)
                                        {
                                            string colName = rowReader.Name;
                                            if (cmd.Parameters.Contains("@" + colName))
                                            {
                                                string val = rowReader.ReadElementContentAsString();
                                                cmd.Parameters["@" + colName].Value = val;
                                            }
                                        }
                                    }
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
                trans.Commit();
                return 1;
            }
            catch (Exception ex)
            {
                throw new CodeEE($"流式导入 DT XML 失败: {ex.Message}");
            }
        }

        // 14. 导出为 MAP XML
        public static long ExportMapXml(string dbName, string tableName, string filePath)
        {
            if (!_connections.TryGetValue(dbName, out var conn)) return 0;

            try
            {
                string fullPath = Path.Combine(Program.ExeDir, filePath);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(fullPath, settings))
                {
                    writer.WriteStartElement("map");
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = $"SELECT k, v FROM {tableName}";
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            writer.WriteStartElement("p");
                            writer.WriteElementString("k", reader.GetString(0));
                            writer.WriteStartElement("v");
                            writer.WriteRaw(reader.IsDBNull(1) ? "" : reader.GetString(1));
                            writer.WriteEndElement(); // v
                            writer.WriteEndElement(); // p
                        }
                    }
                    writer.WriteEndElement(); // map
                }
                return 1;
            }
            catch (Exception ex)
            {
                throw new CodeEE($"导出 MAP XML 失败: {ex.Message}");
            }
        }

        // 15. 导出为 DT XML
        public static long ExportDtXml(string dbName, string tableName, string schemaPath, string dataPath)
        {
            if (!_connections.TryGetValue(dbName, out var conn)) return 0;

            try
            {
                // 为保持兼容性，先通过 DataTable 生成 Schema
                DataTable dt = new DataTable(tableName);
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT * FROM {tableName} LIMIT 0"; // 只取结构
                    using var reader = cmd.ExecuteReader();
                    dt.Load(reader);
                }
                
                string fullSchema = Path.Combine(Program.ExeDir, schemaPath);
                string fullData = Path.Combine(Program.ExeDir, dataPath);
                foreach (var path in new[] { fullSchema, fullData })
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                dt.WriteXmlSchema(fullSchema);

                // 流式导出数据，避免大数据集撑爆内存
                XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
                using (XmlWriter writer = XmlWriter.Create(fullData, settings))
                {
                    writer.WriteStartElement("DocumentElement"); // DataTable 默认根节点
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = $"SELECT * FROM {tableName}";
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            writer.WriteStartElement(tableName);
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                string colName = reader.GetName(i);
                                if (!reader.IsDBNull(i))
                                    writer.WriteElementString(colName, reader.GetValue(i).ToString());
                            }
                            writer.WriteEndElement();
                        }
                    }
                    writer.WriteEndElement();
                }
                return 1;
            }
            catch (Exception ex)
            {
                throw new CodeEE($"导出 DT XML 失败: {ex.Message}");
            }
        }
        // 16. [通用流式导入] 根据指定的节点路径和列映射导入 XML (支持非结构化 XML)
        // columnMappings 格式: "ColName1=@attr,ColName2=childNode,ColName3=childNode(xml)"
        public static long ImportXmlCustom(string dbName, string tableName, string filePath, string rowXPath, string columnMappings)
        {
            if (!_connections.TryGetValue(dbName, out var conn)) return 0;
            string fullPath = Path.Combine(Program.ExeDir, filePath);
            if (!File.Exists(fullPath)) return 0;

            try
            {
                // 解析映射关系
                var mapping = columnMappings.Split(',')
                    .Select(s => s.Split('='))
                    .ToDictionary(a => a[0].Trim(), a => a[1].Trim());

                using var trans = conn.BeginTransaction();

                // 1. 动态建表 (全部设为 TEXT)
                string colDef = string.Join(", ", mapping.Keys.Select(k => $"{k} TEXT"));
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {tableName} ({colDef});";
                    cmd.ExecuteNonQuery();
                }

                // 2. 准备插入语句
                using (var cmd = conn.CreateCommand())
                {
                    string colNames = string.Join(", ", mapping.Keys);
                    string paramNames = string.Join(", ", mapping.Keys.Select(k => "@" + k));
                    cmd.CommandText = $"INSERT INTO {tableName} ({colNames}) VALUES ({paramNames});";
                    
                    foreach (var col in mapping.Keys)
                        cmd.Parameters.Add("@" + col, SqliteType.Text);

                    // 3. 流式读取匹配行
                    // 我们假设 rowXPath 是简单的 "/root/node" 格式
                    string[] parts = rowXPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    string targetNodeName = parts.Last();

                    using (XmlReader reader = XmlReader.Create(fullPath))
                    {
                        while (reader.Read())
                        {
                            if (reader.NodeType == XmlNodeType.Element && reader.Name == targetNodeName)
                            {
                                // 加载当前行节点到小型 XmlDocument 进行 XPath 解析
                                using (XmlReader subReader = reader.ReadSubtree())
                                {
                                    XmlDocument rowDoc = new XmlDocument();
                                    rowDoc.Load(subReader);
                                    XmlNode row = rowDoc.DocumentElement;

                                    foreach (var map in mapping)
                                    {
                                        string expr = map.Value;
                                        string value = null;

                                        if (expr.StartsWith("@"))
                                        {
                                            value = row.Attributes[expr.Substring(1)]?.Value;
                                        }
                                        else if (expr.EndsWith("(xml)"))
                                        {
                                            string nodeName = expr.Substring(0, expr.Length - 5);
                                            value = row.SelectSingleNode(nodeName)?.InnerXml;
                                        }
                                        else
                                        {
                                            value = row.SelectSingleNode(expr)?.InnerText;
                                        }

                                        cmd.Parameters["@" + map.Key].Value = (object)value ?? DBNull.Value;
                                    }
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
                trans.Commit();
                return 1;
            }
            catch (Exception ex)
            {
                throw new CodeEE($"自定义 XML 导入失败: {ex.Message}");
            }
        }

		public static string Escape(string input)
		{
			if (string.IsNullOrEmpty(input)) return input;
			return input.Replace("'", "''");
		}
	}
}
