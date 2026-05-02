using System.Reflection.Emit;

namespace StarterApp.Tests.Consistency;

/// <summary>
/// Walks IL byte arrays on instruction boundaries, yielding each opcode and its operand
/// position/size. Uses System.Reflection.Emit.OpCodes metadata for authoritative operand
/// sizes. Shared by AstShingleComparer (opcode extraction) and SourceTokenEmbedder (token
/// extraction) to avoid duplicating operand-size logic.
/// </summary>
public static class IlInstructionWalker
{
    private static readonly int[] SingleByteOperandSizes = BuildSingleByteTable();
    private static readonly int[] TwoByteOperandSizes = BuildTwoByteTable();

    /// <summary>
    /// Walks the IL byte array, calling the visitor for each instruction.
    /// The visitor receives: (opcode byte, second opcode byte for 0xFE prefix or 0,
    /// operand start offset, operand size in bytes).
    /// </summary>
    public static void Walk(byte[] il, Action<byte, byte, int, int> visitor)
    {
        var i = 0;
        while (i < il.Length)
        {
            var b = il[i];

            if (b == 0xFE && i + 1 < il.Length)
            {
                var b2 = il[i + 1];
                var operandSize = b2 < TwoByteOperandSizes.Length ? TwoByteOperandSizes[b2] : 0;
                var operandStart = i + 2;
                visitor(b, b2, operandStart, operandSize);
                i = operandStart + operandSize;
            }
            else if (b == 0x45) // switch: variable-length
            {
                if (i + 4 < il.Length)
                {
                    var n = BitConverter.ToInt32(il, i + 1);
                    var totalOperandSize = 4 + n * 4;
                    visitor(b, 0, i + 1, totalOperandSize);
                    i += 1 + totalOperandSize;
                }
                else
                {
                    visitor(b, 0, i + 1, 0);
                    i = il.Length; // truncated, bail
                }
            }
            else
            {
                var operandSize = SingleByteOperandSizes[b];
                visitor(b, 0, i + 1, operandSize);
                i += 1 + operandSize;
            }
        }
    }

    /// <summary>
    /// Returns the operand byte count for a given opcode.
    /// Returns -1 for switch (0x45) to signal variable length.
    /// </summary>
    public static int GetOperandSize(byte opcode, byte secondByte = 0)
    {
        if (opcode == 0xFE)
            return secondByte < TwoByteOperandSizes.Length ? TwoByteOperandSizes[secondByte] : 0;

        if (opcode == 0x45)
            return -1;

        return SingleByteOperandSizes[opcode];
    }

    private static int[] BuildSingleByteTable()
    {
        var table = new int[256];

        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode))
                continue;

            var op = (OpCode)field.GetValue(null)!;
            if (op.Size != 1)
                continue;

            var index = (byte)op.Value;
            table[index] = OperandSize(op.OperandType);
        }

        return table;
    }

    private static int[] BuildTwoByteTable()
    {
        var table = new int[256];

        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(OpCode))
                continue;

            var op = (OpCode)field.GetValue(null)!;
            if (op.Size != 2)
                continue;

            var index = (byte)(op.Value & 0xFF);
            table[index] = OperandSize(op.OperandType);
        }

        return table;
    }

    private static int OperandSize(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget => 1,
        OperandType.ShortInlineI => 1,
        OperandType.ShortInlineVar => 1,
        OperandType.InlineBrTarget => 4,
        OperandType.InlineField => 4,
        OperandType.InlineI => 4,
        OperandType.InlineMethod => 4,
        OperandType.InlineSig => 4,
        OperandType.InlineString => 4,
        OperandType.InlineTok => 4,
        OperandType.InlineType => 4,
        OperandType.ShortInlineR => 4,
        OperandType.InlineI8 => 8,
        OperandType.InlineR => 8,
        OperandType.InlineVar => 2,
        OperandType.InlineSwitch => 0, // handled specially in Walk()
        _ => 0
    };
}
