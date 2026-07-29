using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements;

namespace MinorShift.Emuera.Runtime.Script;

internal sealed class ExecutionContext
{
    public FunctionLabelLine Function { get; }
    public long[] LocalIntegers { get; }
    public string[] LocalStrings { get; }
    public double[] LocalFloats { get; }
    public long[] ArgIntegers { get; set; }
    public string[] ArgStrings { get; set; }
    public double[] ArgFloats { get; set; }

    private ExecutionContext _parent;
    private readonly List<ExecutionContext> _children = new();

    public ExecutionContext(FunctionLabelLine func, ExecutionContext parent)
    {
        Function = func;
        _parent = parent;
        _parent?._children.Add(this);

        var idDict = GlobalStatic.IdentifierDictionary;
        int localLen = func.LocalLength;
        int localsLen = func.LocalsLength;
        int localFloatLen = func.LocalFloatLength;
        int argLen = func.ArgLength;
        int argsLen = func.ArgsLength;
        int argFloatLen = func.ArgFloatLength;

        if (idDict != null)
        {
            int defaultLocal = idDict.getLocalDefaultSize("LOCAL");
            int defaultLocals = idDict.getLocalDefaultSize("LOCALS");
            int defaultArg = idDict.getLocalDefaultSize("ARG");
            int defaultArgs = idDict.getLocalDefaultSize("ARGS");
            int defaultLocalF = idDict.getLocalDefaultSize("LOCALF");
            int defaultArgF = idDict.getLocalDefaultSize("ARGF");

            if (localLen <= 0)
                localLen = defaultLocal;
            if (localsLen <= 0)
                localsLen = defaultLocals;
            if (argLen <= 0)
                argLen = defaultArg;
            else if (argLen < defaultArg)
                argLen = defaultArg;
            if (argsLen <= 0)
                argsLen = defaultArgs;
            else if (argsLen < defaultArgs)
                argsLen = defaultArgs;
            if (localFloatLen <= 0)
                localFloatLen = defaultLocalF;
            if (argFloatLen <= 0)
                argFloatLen = defaultArgF;
            else if (argFloatLen < defaultArgF)
                argFloatLen = defaultArgF;

            LocalFloats = localFloatLen > 0 ? new double[localFloatLen] : [];
            ArgFloats = argFloatLen > 0 ? new double[argFloatLen] : [];
        }
        else
        {
            if (localLen <= 0) localLen = 1000;
            if (localsLen <= 0) localsLen = 100;
            if (argLen <= 0) argLen = 1000;
            else if (argLen < 1000) argLen = 1000;
            if (argsLen <= 0) argsLen = 100;
            else if (argsLen < 100) argsLen = 100;

            LocalFloats = localFloatLen > 0 ? new double[localFloatLen] : [];
            ArgFloats = argFloatLen > 0 ? new double[argFloatLen] : [];
        }

        LocalIntegers = new long[localLen];
        LocalStrings = new string[localsLen];
        ArgIntegers = new long[argLen];
        ArgStrings = new string[argsLen];
    }

    public ExecutionContext Parent => _parent;

    public void Dispose()
    {
        _parent?._children.Remove(this);
        foreach (var child in _children)
            child._parent = null;
        _children.Clear();
    }
}
