using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;

namespace MinorShift.Emuera.Runtime.Script.Statements;

internal sealed class NullRefTerm : VariableToken
{
    public NullRefTerm(bool isInteger)
        : base(isInteger ? VariableCode.REF : VariableCode.REFS, null)
    {
        varName = "(null ref)";
        IsReference = true;
        Dimension = 0;
        CanRestructure = true;
    }

    public NullRefTerm(bool isInteger, bool isFloat)
        : base(isFloat ? VariableCode.REFF : (isInteger ? VariableCode.REF : VariableCode.REFS), null)
    {
        varName = "(null ref)";
        IsReference = true;
        Dimension = 0;
        CanRestructure = true;
    }

    public override long GetIntValue(ExpressionMediator exm, long[] arguments) => 0;
    public override string GetStrValue(ExpressionMediator exm, long[] arguments) => "";
    public override double GetFloatValue(ExpressionMediator exm, long[] arguments) => 0.0;
    public override void SetValue(long value, long[] arguments) { }
    public override void SetValue(string value, long[] arguments) { }
    public override void SetValue(double value, long[] arguments) { }
    public override void SetValue(long[] values, long[] arguments) { }
    public override void SetValue(string[] values, long[] arguments) { }
    public override void SetValue(double[] values, long[] arguments) { }
    public override void SetValueAll(long value, int start, int end, int charaPos) { }
    public override void SetValueAll(string value, int start, int end, int charaPos) { }
    public override void SetValueAll(double value, int start, int end, int charaPos) { }
    public override long PlusValue(long value, long[] arguments) => 0;
    public override double PlusValue(double value, long[] arguments) => 0.0;
    public override void CheckElement(long[] arguments, bool[] doCheck) { }
    public override void IsArrayRangeValid(long[] arguments, long index1, long index2, string funcName, long i1, long i2) { }
    public override object GetArray() => null;
}
