using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using System;

namespace MinorShift.Emuera.UI.Game.Image;

internal static class ColorMatrixHelper
{
	public static float[]? ReadFromVariableTerm(AExpression expr, ExpressionMediator exm)
	{
		if (expr is not VariableTerm varTerm)
			return null;
		var p = varTerm.GetFixedVariableTerm(exm);
		return ReadFromVariable(p.Identifier, p.Index1, p.Index2, p.Index3);
	}

	public static float[]? ReadFromVariable(VariableToken token, long idx1, long idx2, long idx3)
	{
		if (token == null)
			return null;

		float[][] cm = new float[5][];

		if (token.IsArray2D && !token.IsFloat)
		{
			long[,] array;
			long e1, e2;
			if (token.IsCharacterData)
			{
				array = token.GetArrayChara((int)idx1) as long[,];
				e1 = idx2;
				e2 = idx3;
			}
			else
			{
				array = token.GetArray() as long[,];
				e1 = idx1;
				e2 = idx2;
			}
			if (array == null || e1 < 0 || e2 < 0 || e1 + 5 > array.GetLength(0) || e2 + 5 > array.GetLength(1))
				return null;
			for (int i = 0; i < 5; i++)
			{
				cm[i] = new float[5];
				for (int j = 0; j < 5; j++)
					cm[i][j] = array[e1 + i, e2 + j] / 256f;
			}
		}
		else if (token.IsArray3D && !token.IsFloat)
		{
			long[,,] array;
			if (token.IsCharacterData)
				return null;
			array = token.GetArray() as long[,,];
			long e1 = idx1, e2 = idx2, e3 = idx3;
			if (array == null || e1 < 0 || e1 >= array.GetLength(0) || e2 < 0 || e3 < 0 || e2 + 5 > array.GetLength(1) || e3 + 5 > array.GetLength(2))
				return null;
			for (int i = 0; i < 5; i++)
			{
				cm[i] = new float[5];
				for (int j = 0; j < 5; j++)
					cm[i][j] = array[e1, e2 + i, e3 + j] / 256f;
			}
		}
		else
		{
			return null;
		}

		return ToSkia(cm);
	}

	public static float[] ToSkia(float[][] cm)
	{
		return [
			cm[0][0], cm[0][1], cm[0][2], cm[0][3], cm[0][4] * 255f,
			cm[1][0], cm[1][1], cm[1][2], cm[1][3], cm[1][4] * 255f,
			cm[2][0], cm[2][1], cm[2][2], cm[2][3], cm[2][4] * 255f,
			cm[3][0], cm[3][1], cm[3][2], cm[3][3], cm[3][4] * 255f,
		];
	}
}
