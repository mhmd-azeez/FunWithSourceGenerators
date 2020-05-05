using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FunWithSourceGenerators
{
    [Generator]
    class AsyncifyGenerator : ISourceGenerator
    {
        private const string AttributeText = @"
namespace System
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    sealed class AsyncifyAttribute : Attribute
    {
        public AsyncifyAttribute()
        {
        }
    }
}
";
        public void Execute(SourceGeneratorContext context)
        {
            // add the attribute text
            context.AddSource("AsyncifyAttribute", SourceText.From(AttributeText, Encoding.UTF8));

            // retreive the populated receiver 
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
                return;

            // we're going to create a new compilation that contains the attribute.
            // TODO: we should allow source generators to provide source during initialize, so that this step isn't required.
            CSharpParseOptions options = (context.Compilation as CSharpCompilation).SyntaxTrees[0].Options as CSharpParseOptions;
            Compilation compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(AttributeText, Encoding.UTF8), options));

            // get the newly bound attribute, and INotifyPropertyChanged
            INamedTypeSymbol attributeSymbol = compilation.GetTypeByMetadataName("System.AsyncifyAttribute");

            // loop over the candidate fields, and keep the ones that are actually annotated
            List<IMethodSymbol> methodSymbols = new List<IMethodSymbol>();
            foreach (var method in receiver.CandidateMethods)
            {
                SemanticModel model = compilation.GetSemanticModel(method.SyntaxTree);
                var methodSymbol = model.GetDeclaredSymbol(method) as IMethodSymbol;
                if (methodSymbol.GetAttributes().Any(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default)))
                {
                    methodSymbols.Add(methodSymbol);
                }
            }

            foreach (var group in methodSymbols.GroupBy(f => f.ContainingType))
            {
                string classSource = ProcessClass(group.Key, group.ToList(), context);
                context.AddSource($"{group.Key.Name}_asyncify.cs", SourceText.From(classSource, Encoding.UTF8));
            }
        }

        private string ProcessClass(INamedTypeSymbol classSymbol, List<IMethodSymbol> methods, SourceGeneratorContext context)
        {
            if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
            {
                return null; //TODO: issue a diagnostic that it must be top level
            }

            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            // begin building the generated source
            StringBuilder source = new StringBuilder($@"
using System.Threading.Tasks;

namespace {namespaceName}
{{
    public partial class {classSymbol.Name}
    {{
");

            // create properties for each field 
            foreach (var methodSymbol in methods)
            {
                ProcessMethod(source, methodSymbol, context);
            }

            source.Append("} }");
            return source.ToString();
        }

        private void ProcessMethod(StringBuilder source, IMethodSymbol methodSymbol, SourceGeneratorContext context)
        {
            if (methodSymbol.IsAsync)
            {
                // Can't asyncify async methods!
                return;
            }

            // get the name and type of the field
            string methodName = methodSymbol.Name;
            ITypeSymbol returnType = methodSymbol.ReturnType;

            //// get the AutoNotify attribute from the field, and any associated data
            //AttributeData attributeData = methodSymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));
            //TypedConstant overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;

            string asyncMethodName = $"{methodName}Async";
            if (asyncMethodName.Length == 0 || asyncMethodName == methodName)
            {
                //TODO: issue a diagnostic that we can't process this field
                return;
            }

            var asyncReturnType = "Task";
            var staticModifier = methodSymbol.IsStatic ? "static" : string.Empty;

            if (returnType.Name != "Void")
            {
                asyncReturnType = $"Task<{returnType.Name}>";
            }

            var parameters = string.Join(",", methodSymbol.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var arguments = string.Join(",", methodSymbol.Parameters.Select(p => p.Name));

            source.Append($@"
public {staticModifier} {asyncReturnType} {asyncMethodName}({parameters})
{{
    return Task.Run(() => {methodName}({arguments}));
}}
");
        }

        public void Initialize(InitializationContext context)
        {
            // Register a factory that can create our custom syntax receiver
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }
    }

    /// <summary>
    /// Created on demand before each generation pass
    /// </summary>
    class SyntaxReceiver : ISyntaxReceiver
    {
        public List<MethodDeclarationSyntax> CandidateMethods { get; } = new List<MethodDeclarationSyntax>();

        /// <summary>
        /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
        /// </summary>
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            // any method with at least one attribute is a candidate for property generation
            if (syntaxNode is MethodDeclarationSyntax methodDeclarationSyntax
                && methodDeclarationSyntax.AttributeLists.Count > 0)
            {
                CandidateMethods.Add(methodDeclarationSyntax);
            }
        }
    }
}
