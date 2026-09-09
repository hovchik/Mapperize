using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Mapperize.Generator;

/// <summary>
/// Roslyn incremental source generator that implements the <c>partial</c> mapping methods of
/// every class annotated with <c>[Mapperize.Mapper]</c>. All mapping code is emitted at compile
/// time as direct member assignments — no reflection, no runtime configuration, AOT/trim safe.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MapperGenerator : IIncrementalGenerator
{
    private const string MapperAttribute = "Mapperize.MapperAttribute";
    private const string MapPropertyAttribute = "Mapperize.MapPropertyAttribute";
    private const string MapperIgnoreTargetAttribute = "Mapperize.MapperIgnoreTargetAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = context.SyntaxProvider.ForAttributeWithMetadataName(
            MapperAttribute,
            predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
            transform: static (ctx, ct) => Build(ctx, ct));

        context.RegisterSourceOutput(results, static (spc, result) =>
        {
            if (result is null)
                return;

            foreach (var diag in result.Diagnostics)
                spc.ReportDiagnostic(diag);

            if (result.Source is not null)
                spc.AddSource(result.HintName, result.Source);
        });
    }

    private static MapperResult? Build(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol mapper)
            return null;

        var emitter = new Emitter(ctx.SemanticModel.Compilation, mapper, ctx.Attributes[0]);
        return emitter.Emit(ct);
    }
}

internal sealed record MapperResult(string HintName, string? Source, ImmutableArray<Diagnostic> Diagnostics);
