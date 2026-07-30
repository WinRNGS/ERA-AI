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

	/// <summary>本轮由调度器直接回写的数值变更（P0 假后端路径使用）。</summary>
	public List<AiValueChange> Changes = [];

	/// <summary>本轮标识，与副 API 的 turn_id 一致。</summary>
	public string TurnId;

	/// <summary>副 API 已落盘的变更（含写入前的值，可用于回滚）。</summary>
	public List<Compute.AiAppliedChange> ComputeApplied = [];

	/// <summary>副 API 的结果提示，已并入主 API 的本轮输入。</summary>
	public string ComputeHint;

	/// <summary>副 API 自报的不确定项。</summary>
	public List<string> ComputeWarnings = [];

	/// <summary>副 API 未参与本轮的原因（正常跳过或失败），非空时数值未被改动。</summary>
	public string ComputeSkipReason;

	/// <summary>
	/// 数值已写入但叙事失败（RISK-05）。此时 ComputeApplied 非空且存档已变，
	/// 调度器会保留一份待处置事务，供「仅重生成文本」或「回滚本轮数值」使用。
	/// </summary>
	public bool NarrativeFailedAfterApply;

	/// <summary>终止或失败后是否已把数值回滚回写入前的状态。</summary>
	public bool ComputeRolledBack;

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