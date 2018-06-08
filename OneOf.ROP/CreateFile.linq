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

public static readonly string _newLine = "\r\n";
public static readonly int _spacesPerTab = 4;
private static string GetTabs(int number) => new string('\t', number);

private static (int left, int right) Split(int input) {
    var result = input / 2;
    return (result, input - result);
}


private string GetGenericArgs(int startIndex, int numberOfArgs) 
    => GetItemsWithArgNumber(startIndex, numberOfArgs, e => $"T{e}");
private string GetItems(int startIndex, int numberOfArgs, string item)
    => GetItemsWithArgNumber(startIndex, numberOfArgs, _ => item);
private string GetItemsWithArgNumber(int startIndex, int numberOfArgs, Func<int, string> mapFunc)
    => Join(Enumerable.Range(startIndex, numberOfArgs).Select(mapFunc));
    
private string Join<T>(IEnumerable<T> inputs) => string.Join(", ", inputs);

private string BuildResultVariable(int index) => $"result{index}";
private string BuildValueVariable(int index) => $"item{index}";

private string _mergeFunc = "mergeFunc.ThrowIfDefault(nameof(mergeFunc))";
string BuildPlus(int start, int length, bool withMergeFunc){
    var remainder = withMergeFunc ? $", {_mergeFunc}" : "";
    if(length == 1){
        return BuildResultVariable(start);
    }
    return $"Result.Plus({GetItemsWithArgNumber(start, length, BuildResultVariable)}{remainder})";
}

private string BuildPlusErrorExtensionMethod(int numberOfArgs){
    var genericArgs = GetGenericArgs(0, numberOfArgs);
    var inputs = GetItemsWithArgNumber(0, numberOfArgs, x => $"Result<T{x}> {BuildResultVariable(x)}");
    var (left, right) = Split(numberOfArgs);
    var firstPlusArgs = GetItemsWithArgNumber(1, numberOfArgs - 2, BuildResultVariable);
    return $@"
        public static Result<({genericArgs})>
            Plus<{genericArgs}>
                (this {inputs})
                    => Result.Plus({BuildPlus(0, left, false)}, {BuildPlus(left, right, false)}).Map(TupleHelper.Unfold);";
}

private string BuildPlusExtensionMethod(int numberOfArgs){
    var genericArgs = GetGenericArgs(0, numberOfArgs);
    var (left, right) = Split(numberOfArgs);
    var inputs = GetItemsWithArgNumber(0, numberOfArgs, x => $"Result<T{x}, TError> {BuildResultVariable(x)}");
    return $@"
        public static Result<({genericArgs}), TError>
            Plus<{genericArgs}, TError>
                (this {inputs}, Func<TError, TError, TError> mergeFunc)
                    => Result.Plus({BuildPlus(0, left, true)}, {BuildPlus(left, right, true)}, {_mergeFunc}).Map(TupleHelper.Unfold);

        public static Result<({genericArgs}), TError>
            Plus<{genericArgs}, TError>
                (this {inputs}) where TError : IPlus<TError, TError>
                    => Result.Plus({BuildPlus(0, left, false)}, {BuildPlus(left, right, false)}).Map(TupleHelper.Unfold);";
}

private string BuildTupleDeconstructMethods((int, int) input){
    var (left, right) = input;
    string TryWrap(string value, int numberOfArgs) => numberOfArgs != 1 ? $"({value})" : value; 
    var genericArgs = GetGenericArgs(0, left + right);
    var leftArgs = TryWrap(GetGenericArgs(0, left), left);
    var rightArgs = TryWrap(GetGenericArgs(left, right), right);
    var leftItems = GetItemsWithArgNumber(0, left, BuildValueVariable); 
    var rightItems = GetItemsWithArgNumber(left, right, BuildValueVariable);
    return $@"
        public static({genericArgs})
            Unfold<{genericArgs}>
                (this
                    ({leftArgs}, {rightArgs})
                    item)
        {{
            var ({TryWrap(leftItems, left)}, {TryWrap(rightItems, right)}) = item;
            return ({leftItems}, {rightItems});
        }}";
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
    var items = Enumerable
        .Range(3, 6)
        .SelectMany(x => 
            Enumerable
                .Range(1, x / 2)
                .SelectMany(y =>  new [] { (x - y, y), (y, x - y)}).Distinct());
    var sb = new StringBuilder();
    sb.Append(@"using System;

namespace OneOf.ROP.Utils
{
    internal static class TupleHelper
    {");
        foreach(var i in items)
        {
            sb.AppendLine(BuildTupleDeconstructMethods(i));
        }
        
    sb.AppendLine(@"
    }
}");
    return sb.ToString();
}