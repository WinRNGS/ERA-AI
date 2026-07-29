using MinorShift.Emuera.GameData.Variable;

namespace MinorShift.Emuera.Runtime.Script.Statements;

internal readonly struct ElementRefInfo
{
    public readonly VariableToken TargetVar;
    public readonly long[] Indices;
    public readonly object CapturedArray;

    public ElementRefInfo(VariableToken targetVar, long[] indices)
    {
        TargetVar = targetVar;
        Indices = indices;
        if (targetVar != null && !targetVar.IsCharacterData && targetVar.Dimension > 0)
            CapturedArray = targetVar.GetArray();
        else
            CapturedArray = null;
    }

    public bool IsNull => TargetVar == null;

    public void SetValueInt(long value)
    {
        if (CapturedArray is long[] arr1d && Indices.Length == 1)
            arr1d[Indices[0]] = value;
        else if (CapturedArray is SparseArray<long> sa1d && Indices.Length == 1)
            sa1d[Indices[0]] = value;
        else if (CapturedArray is long[,] arr2d && Indices.Length == 2)
            arr2d[Indices[0], Indices[1]] = value;
        else if (CapturedArray is long[,,] arr3d && Indices.Length == 3)
            arr3d[Indices[0], Indices[1], Indices[2]] = value;
        else
            TargetVar.SetValue(value, Indices);
    }

    public long GetValueInt(ExpressionMediator exm)
    {
        if (CapturedArray is long[] arr1d && Indices.Length == 1)
            return arr1d[Indices[0]];
        if (CapturedArray is SparseArray<long> sa1d && Indices.Length == 1)
            return sa1d[Indices[0]];
        if (CapturedArray is long[,] arr2d && Indices.Length == 2)
            return arr2d[Indices[0], Indices[1]];
        if (CapturedArray is long[,,] arr3d && Indices.Length == 3)
            return arr3d[Indices[0], Indices[1], Indices[2]];
        return TargetVar.GetIntValue(exm, Indices);
    }

    public void SetValueStr(string value)
    {
        if (CapturedArray is string[] arr1d && Indices.Length == 1)
            arr1d[Indices[0]] = value;
        else if (CapturedArray is SparseArray<string> sa1d && Indices.Length == 1)
            sa1d[Indices[0]] = value;
        else if (CapturedArray is string[,] arr2d && Indices.Length == 2)
            arr2d[Indices[0], Indices[1]] = value;
        else if (CapturedArray is string[,,] arr3d && Indices.Length == 3)
            arr3d[Indices[0], Indices[1], Indices[2]] = value;
        else
            TargetVar.SetValue(value, Indices);
    }

    public string GetValueStr(ExpressionMediator exm)
    {
        if (CapturedArray is string[] arr1d && Indices.Length == 1)
            return arr1d[Indices[0]];
        if (CapturedArray is SparseArray<string> sa1d && Indices.Length == 1)
            return sa1d[Indices[0]];
        if (CapturedArray is string[,] arr2d && Indices.Length == 2)
            return arr2d[Indices[0], Indices[1]];
        if (CapturedArray is string[,,] arr3d && Indices.Length == 3)
            return arr3d[Indices[0], Indices[1], Indices[2]];
        return TargetVar.GetStrValue(exm, Indices);
    }

    public void SetValueFloat(double value)
    {
        if (CapturedArray is double[] arr1d && Indices.Length == 1)
            arr1d[Indices[0]] = value;
        else if (CapturedArray is SparseArray<double> sa1d && Indices.Length == 1)
            sa1d[Indices[0]] = value;
        else if (CapturedArray is double[,] arr2d && Indices.Length == 2)
            arr2d[Indices[0], Indices[1]] = value;
        else if (CapturedArray is double[,,] arr3d && Indices.Length == 3)
            arr3d[Indices[0], Indices[1], Indices[2]] = value;
        else
            TargetVar.SetValue(value, Indices);
    }

    public double GetValueFloat(ExpressionMediator exm)
    {
        if (CapturedArray is double[] arr1d && Indices.Length == 1)
            return arr1d[Indices[0]];
        if (CapturedArray is SparseArray<double> sa1d && Indices.Length == 1)
            return sa1d[Indices[0]];
        if (CapturedArray is double[,] arr2d && Indices.Length == 2)
            return arr2d[Indices[0], Indices[1]];
        if (CapturedArray is double[,,] arr3d && Indices.Length == 3)
            return arr3d[Indices[0], Indices[1], Indices[2]];
        return TargetVar.GetFloatValue(exm, Indices);
    }

    public long PlusValueInt(long value)
    {
        if (CapturedArray is long[] arr1d && Indices.Length == 1)
        {
            arr1d[Indices[0]] += value;
            return arr1d[Indices[0]];
        }
        if (CapturedArray is SparseArray<long> sa1d && Indices.Length == 1)
        {
            sa1d[Indices[0]] += value;
            return sa1d[Indices[0]];
        }
        TargetVar.PlusValue(value, Indices);
        return TargetVar.GetIntValue(null, Indices);
    }

    public override string ToString()
    {
        if (IsNull)
            return "ElementRefInfo(null)";
        
        var indicesStr = Indices != null ? string.Join(",", Indices) : "null";
        return $"ElementRefInfo({TargetVar.Name}[{indicesStr}])";
    }
}
