using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MinorShift.Emuera.AI;

/// <summary>
/// 一次 AI 请求的结果。后台线程只负责把网络与解析产物塞进这个对象，
/// 绝不触碰任何 ERA 变量或界面状态。
/// </summary>
internal sealed class AiTurnResult
{
	/// <summary>请求票号，用于丢弃过期回调。</summary>
	public long Ticket;

	/// <summary>是否成功拿到可用结果。</summary>
	public bool Success;

	/// <summary>是否因玩家终止而结束。</summary>
	public bool Aborted;

	/// <summary>主 API 产出的叙事正文。</summary>
	public string NarrativeText = "";

	/// <summary>副 API 产出的数值变更集（P0 阶段为假数据）。</summary>
	public List<AiValueChange> Changes = [];

	/// <summary>失败原因，仅用于展示与日志，不含密钥。</summary>
	public string ErrorMessage;

	/// <summary>后台耗时，用于观测延迟。</summary>
	public long ElapsedMs;
}

/// <summary>
/// 单条数值变更指令。对应设计文档 S3.8 的回写契约。
/// </summary>
internal sealed class AiValueChange
{
	/// <summary>目标变量表达式，形如 CFLAG:5:好感度 或 MONEY。</summary>
	public string Target;

	/// <summary>操作符：set / add / mul。</summary>
	public string Op = "set";

	/// <summary>整数变更量。</summary>
	public long Value;

	/// <summary>字符串赋值时使用（Op 必须为 set）。</summary>
	public string StrValue;

	/// <summary>是否为字符串赋值。</summary>
	public bool IsStringAssign;
}