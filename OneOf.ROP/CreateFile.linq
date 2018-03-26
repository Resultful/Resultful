<Query Kind="Program" />

void Main()
{
    DumpContents("ResultExtensions.Tuple.cs", GetTupleExtensionsContent());
    DumpContents("Utils/TupleHelper.cs", GetTupleHelperContent());
}

public void DumpContents(string path, string content){
    var outpath = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), path);
    var finalContent = content.Replace("\t", new string(' ', _spacesPerTab));
    File.WriteAllText(outpath.Dump(), finalContent.Dump());
}

public string Join(IEnumerable<string> items){
    return string.Join(", ", items);
}
public static readonly string _newLine = "\r\n";
public static readonly int _spacesPerTab = 4;
private static string GetTabs(int number) => new string('\t', number);


private List<string> GetGenericArgs(int startIndex, int numberOfArgs)
    => Enumerable.Range(startIndex, numberOfArgs).Select(e => $"T{e}").ToList();
private List<string> GetItems(int numberOfArgs, string item)
    => Enumerable.Range(0, numberOfArgs).Select(x => item).ToList();
private List<string> GetItemsWithArgNumber(int numberOfArgs, Func<int, string> mapFunc)
    => Enumerable.Range(0, numberOfArgs).Select(mapFunc).ToList();

private string BuildResultVariable(int index) => $"result{index}";
private string BuildValueVariable(int index) => $"item{index}";

private string BuildPlusErrorExtensionMethod(int numberOfArgs){
    var genericArgs = GetGenericArgs(0, numberOfArgs);
    var inputs = Enumerable.Range(0, numberOfArgs).Select(x => $"Result<T{x}> {BuildResultVariable(x)}");
    var firstPlusArgs = Enumerable.Range(1, numberOfArgs - 2).Select(BuildResultVariable);
    return $@"
        public static Result<({Join(genericArgs)})>
            Plus<{Join(genericArgs)}>
                (this {Join(inputs)})
                    => result0.Plus({Join(firstPlusArgs)}).Plus({BuildResultVariable(numberOfArgs -1)}).Map(TupleHelper.Unfold);";
}

private string BuildTupleDeconstructMethods(int numberOfArgs){
    var genericArgs = GetGenericArgs(0, numberOfArgs);
    var finalArgIndex = numberOfArgs -1;
    var inputArgs = GetGenericArgs(0, finalArgIndex);
    return $@"
        public static({Join(genericArgs)})
            Unfold<{Join(genericArgs)}>
                (this
                    (({Join(inputArgs)}), T{finalArgIndex})
                    item)
        {{
            var (tuple, item{finalArgIndex}) = item;
            var ({Join(GetItemsWithArgNumber(finalArgIndex, BuildValueVariable))}) = tuple;
            return ({Join(GetItemsWithArgNumber(numberOfArgs, BuildValueVariable))});
        }}";
}

private string BuildPlusExtensionMethod(int numberOfArgs){
    var genericArgs = GetGenericArgs(0, numberOfArgs);
    var inputs = Enumerable.Range(0, numberOfArgs).Select(x => $"Result<T{x}, TError> {BuildResultVariable(x)}");
    var firstPlusArgs = Enumerable.Range(1, numberOfArgs - 2).Select(BuildResultVariable);
    return $@"
        public static Result<({Join(genericArgs)}), TError>
            Plus<{Join(genericArgs)}, TError>
                (this {Join(inputs)}, Func<TError, TError, TError> mergeFunc)
                    => result0.Plus({Join(firstPlusArgs)}, mergeFunc.ThrowIfDefault(nameof(mergeFunc))).Plus({BuildResultVariable(numberOfArgs -1)}, mergeFunc).Map(TupleHelper.Unfold);

        public static Result<({Join(genericArgs)}), TError>
            Plus<{Join(genericArgs)}, TError>
                (this {Join(inputs)}) where TError : IPlus<TError, TError>
                    => result0.Plus({Join(firstPlusArgs)}).Plus({BuildResultVariable(numberOfArgs -1)}).Map(TupleHelper.Unfold);";
}

public string GetTupleExtensionsContent(){
    var sb = new StringBuilder();
    sb.Append(@"using System;
using OneOf.ROP.Utils;

namespace OneOf.ROP
{
    public static partial class Result
    {");
        for (var i = 3; i < 9; i++)
        {
            sb.AppendLine(BuildPlusExtensionMethod(i));
            sb.AppendLine(BuildPlusErrorExtensionMethod(i));
        }
    sb.AppendLine(@"
    }
}");
    return sb.ToString();
}

public string GetTupleHelperContent(){
    var sb = new StringBuilder();
    sb.Append(@"using System;

namespace OneOf.ROP.Utils
{
    internal static class TupleHelper
    {");
        for (var i = 3; i < 9; i++)
        {
            sb.AppendLine(BuildTupleDeconstructMethods(i));
        }
    sb.AppendLine(@"
    }
}");
    return sb.ToString();
}