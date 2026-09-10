using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace TNTc;

/// <summary>A string an assembly displays, and the member it is displayed from.</summary>
public sealed record ScannedLiteral(string Value, string Member);

/// <summary>A call this scanner recognised as a translation but could not read a key from - which is nearly always the <c>$"…".t()</c> mistake, where the interpolation happens before the lookup and the table is asked for a string it can never hold.</summary>
public sealed record ScanFinding(string Member, string Message);

public sealed record AssemblyScanResult(ImmutableArray<ScannedLiteral> Literals, ImmutableArray<ScanFinding> Findings);

/// <summary>
/// Finds the strings an already-compiled assembly will ask TNT to translate, by reading the IL of
/// every method for a literal flowing into <c>TNT.T.t</c>. It exists because a package's sources are
/// not in the repository being translated - <c>extract</c> can only see this project's own code - and
/// yet the strings a referenced package draws are strings the application shows.
///
/// Two shapes are recognised, and they are the two the C# compiler emits for the two entry points:
///
/// <code>
/// ldstr      "Leave without saving"                        //  "…".t()
/// call       string TNT.T::t(string)
///
/// ldstr      "{0} matches"                                 //  t($"…")
/// ldc.i4.1
/// newarr     object
/// …                                                        //  the arguments
/// call       FormattableString FormattableStringFactory::Create(string, object[])
/// call       string TNT.T::t(FormattableString)
/// </code>
///
/// <c>TNT.T</c> is matched by name, not by assembly: every Transpose library declares its own copy of
/// those twenty lines (the TNT.T package cannot be consumed by one), so there is no single identity to
/// match against.
///
/// A key read this way carries the member it was found in rather than a <c>path:line</c>, so a
/// translator cannot open the call site. That is the argument for a package translating itself and
/// shipping the tables instead - this is what to do about the ones that do not.
/// </summary>
public static class AssemblyStringScanner
{
    private const string TRANSLATION_TYPE   = "TNT.T";
    private const string TRANSLATION_METHOD = "t";
    private const string FORMATTABLE_TYPE   = "System.Runtime.CompilerServices.FormattableStringFactory";
    private const string FORMATTABLE_METHOD = "Create";

    private static readonly Dictionary<ushort, OpCode> _opCodes = BuildOpCodeTable();

    public static AssemblyScanResult Scan(string assemblyPath)
    {
        var literals = ImmutableArray.CreateBuilder<ScannedLiteral>();
        var findings = ImmutableArray.CreateBuilder<ScanFinding>();

        using var stream = File.OpenRead(assemblyPath);
        using var pe     = new PEReader(stream);

        if (!pe.HasMetadata) return new AssemblyScanResult(literals.ToImmutable(), findings.ToImmutable());

        var reader = pe.GetMetadataReader();

        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);

            if (method.RelativeVirtualAddress == 0) continue;

            MethodBodyBlock body;

            try
            {
                body = pe.GetMethodBody(method.RelativeVirtualAddress);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            var instructions = Decode(body.GetILContent());

            if (instructions.Count == 0) continue;

            ScanMethod(reader, handle, method, instructions, literals, findings);
        }

        return new AssemblyScanResult(literals.ToImmutable(), findings.ToImmutable());
    }

    private static void ScanMethod(MetadataReader                            reader,
                                   MethodDefinitionHandle                    handle,
                                   MethodDefinition                          method,
                                   List<Instruction>                         instructions,
                                   ImmutableArray<ScannedLiteral>.Builder    literals,
                                   ImmutableArray<ScanFinding>.Builder       findings)
    {
        for (var index = 0; index < instructions.Count; index++)
        {
            if (!IsCall(instructions[index].OpCode)) continue;

            var target = ResolveMethod(reader, instructions[index].Operand);

            if (target is null || target.Value.Method != TRANSLATION_METHOD || target.Value.Type != TRANSLATION_TYPE) continue;

            var member = DescribeMember(reader, handle, method);
            var key    = ReadKey(reader, instructions, index);

            if (key is null)
            {
                findings.Add(new ScanFinding(member, "a translated string is built before it is looked up, so the table is asked for the interpolated text - write t($\"…\") rather than $\"…\".t()"));
                continue;
            }

            literals.Add(new ScannedLiteral(key, member));
        }
    }

    /// <summary>The key a <c>t</c> call looks up: the literal it was handed, or the format a <see cref="FormattableString"/> was built from.</summary>
    private static string? ReadKey(MetadataReader reader, List<Instruction> instructions, int callIndex)
    {
        if (callIndex == 0) return null;

        var previous = instructions[callIndex - 1];

        if (previous.OpCode == (ushort)ILOpCode.Ldstr) return ReadUserString(reader, previous.Operand);

        if (!IsCall(previous.OpCode)) return null;

        var factory = ResolveMethod(reader, previous.Operand);

        if (factory is null || factory.Value.Method != FORMATTABLE_METHOD || factory.Value.Type != FORMATTABLE_TYPE) return null;

        return ReadFormat(reader, instructions, callIndex - 1);
    }

    /// <summary>
    /// The format a <c>FormattableStringFactory.Create</c> call was handed: the literal loaded just
    /// before the arguments array is built. Scanning back for that array rather than for the nearest
    /// literal is what keeps an interpolation holding a literal of its own from being read as the
    /// format; a nested <c>Create</c> encountered on the way owns the next array, so it is skipped.
    /// </summary>
    private static string? ReadFormat(MetadataReader reader, List<Instruction> instructions, int createIndex)
    {
        var nested = 0;

        for (var index = createIndex - 1; index >= 0; index--)
        {
            var instruction = instructions[index];

            if (IsCall(instruction.OpCode))
            {
                var called = ResolveMethod(reader, instruction.Operand);

                if (called is { Method: FORMATTABLE_METHOD, Type: FORMATTABLE_TYPE }) nested++;

                // An empty argument list is 'Array.Empty<object>()' rather than a newarr, so the
                // format is the literal immediately before the call.
                if (called is { Method: "Empty", Type: "System.Array" })
                {
                    if (nested > 0) { nested--; continue; }

                    return index >= 1 && instructions[index - 1].OpCode == (ushort)ILOpCode.Ldstr ? ReadUserString(reader, instructions[index - 1].Operand) : null;
                }

                continue;
            }

            if (instruction.OpCode != (ushort)ILOpCode.Newarr) continue;

            if (nested > 0) { nested--; continue; }

            return index >= 2 && instructions[index - 2].OpCode == (ushort)ILOpCode.Ldstr ? ReadUserString(reader, instructions[index - 2].Operand) : null;
        }

        return null;
    }

    private static bool IsCall(ushort opCode) => opCode == (ushort)ILOpCode.Call || opCode == (ushort)ILOpCode.Callvirt;

    private static string? ReadUserString(MetadataReader reader, int token)
    {
        try
        {
            return reader.GetUserString(MetadataTokens.UserStringHandle(token));
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>The declaring type and name of a called method, whichever table the token points into. Only the names matter here - the two overloads of <c>t</c> are told apart by what the call site loaded, not by a signature.</summary>
    private static (string Type, string Method)? ResolveMethod(MetadataReader reader, int token)
    {
        EntityHandle handle;

        try
        {
            handle = MetadataTokens.EntityHandle(token);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (handle.IsNil) return null;

        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var definition = reader.GetMethodDefinition((MethodDefinitionHandle)handle);

                return (TypeName(reader, definition.GetDeclaringType()), reader.GetString(definition.Name));
            }

            case HandleKind.MemberReference:
            {
                var reference = reader.GetMemberReference((MemberReferenceHandle)handle);
                var parent    = reference.Parent;
                var type      = parent.Kind switch
                {
                    HandleKind.TypeReference  => TypeName(reader, (TypeReferenceHandle)parent),
                    HandleKind.TypeDefinition => TypeName(reader, (TypeDefinitionHandle)parent),
                    _                         => null
                };

                return type is null ? null : (type, reader.GetString(reference.Name));
            }

            case HandleKind.MethodSpecification:
            {
                return ResolveMethod(reader, MetadataTokens.GetToken(reader.GetMethodSpecification((MethodSpecificationHandle)handle).Method));
            }

            default: return null;
        }
    }

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name       = reader.GetString(definition.Name);

        if (definition.IsNested) return $"{TypeName(reader, definition.GetDeclaringType())}+{name}";

        var space = reader.GetString(definition.Namespace);

        return string.IsNullOrEmpty(space) ? name : $"{space}.{name}";
    }

    private static string TypeName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name      = reader.GetString(reference.Name);

        if (reference.ResolutionScope.Kind == HandleKind.TypeReference) return $"{TypeName(reader, (TypeReferenceHandle)reference.ResolutionScope)}+{name}";

        var space = reader.GetString(reference.Namespace);

        return string.IsNullOrEmpty(space) ? name : $"{space}.{name}";
    }

    private static string DescribeMember(MetadataReader reader, MethodDefinitionHandle handle, MethodDefinition method)
        => $"{TypeName(reader, method.GetDeclaringType())}.{reader.GetString(method.Name)}";

    private readonly record struct Instruction(ushort OpCode, int Operand);

    /// <summary>Splits a method body into its instructions. Only the opcode and a token-sized operand are kept - nothing here needs to know what any instruction does, only which literal was loaded before which call.</summary>
    private static List<Instruction> Decode(ImmutableArray<byte> il)
    {
        var instructions = new List<Instruction>();
        var at           = 0;

        while (at < il.Length)
        {
            ushort code = il[at++];

            if (code == 0xFE)
            {
                if (at >= il.Length) break;

                code = (ushort)(0xFE00 | il[at++]);
            }

            if (!_opCodes.TryGetValue(code, out var opCode)) return instructions; // an opcode we cannot size means we no longer know where the next one starts

            var operandSize = OperandSize(opCode.OperandType);

            if (opCode.OperandType == OperandType.InlineSwitch)
            {
                if (at + 4 > il.Length) break;

                operandSize = 4 + 4 * ReadInt32(il, at);
            }

            if (at + operandSize > il.Length) break;

            instructions.Add(new Instruction(code, operandSize == 4 ? ReadInt32(il, at) : 0));

            at += operandSize;
        }

        return instructions;
    }

    private static int ReadInt32(ImmutableArray<byte> il, int at) => il[at] | (il[at + 1] << 8) | (il[at + 2] << 16) | (il[at + 3] << 24);

    private static int OperandSize(OperandType operandType) => operandType switch
    {
        OperandType.InlineNone                                                                                                        => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar                                     => 1,
        OperandType.InlineVar                                                                                                         => 2,
        OperandType.InlineI8 or OperandType.InlineR                                                                                   => 8,
        _                                                                                                                             => 4
    };

    /// <summary>The runtime's own opcode table, keyed the way an IL stream spells each opcode, so nothing here hand-maintains one.</summary>
    private static Dictionary<ushort, OpCode> BuildOpCodeTable()
    {
        var table = new Dictionary<ushort, OpCode>();

        foreach (var field in typeof(OpCodes).GetFields())
        {
            if (field.GetValue(null) is not OpCode opCode) continue;

            table[unchecked((ushort)opCode.Value)] = opCode;
        }

        return table;
    }
}
