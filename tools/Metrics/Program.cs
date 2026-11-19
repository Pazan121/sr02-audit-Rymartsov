// Вимірювач метрик для СР-2 за методикою М1-М3: цикломатична складність,
// логічні рядки, вкладеність керувальних конструкцій, кількість параметрів,
// fan-out типу і клони від шести рядків. Код розбирається синтаксичним
// деревом Roslyn, тому коментарі та рядкові літерали на числа не впливають.
// Запуск: dotnet run --project tools/Metrics -- src docs/metrics-after.csv
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

string srcDir = args.Length > 0 ? args[0] : "src";
string csvPath = args.Length > 1 ? args[1] : "metrics.csv";

if (!Directory.Exists(srcDir))
{
    Console.WriteLine($"Немає теки: {srcDir}");
    return 1;
}

var files = Directory.GetFiles(srcDir, "*.cs", SearchOption.TopDirectoryOnly)
    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
    .ToList();

if (files.Count == 0)
{
    Console.WriteLine($"У теці немає .cs-файлів: {srcDir}");
    return 1;
}

var sources = files.ToDictionary(f => f, f => File.ReadAllText(f));
var trees = files.Select(f => CSharpSyntaxTree.ParseText(sources[f], path: f)).ToList();

// Типи самого проєкту — за ними рахується fan-out.
var projectTypes = trees
    .SelectMany(t => t.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
    .Select(t => t.Identifier.ValueText)
    .ToHashSet(StringComparer.Ordinal);

var methods = new List<Row>();

foreach (var tree in trees)
{
    string fileName = Path.GetFileName(tree.FilePath);

    foreach (var declaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
    {
        int fanOut = FanOut(declaration, projectTypes);

        foreach (var member in declaration.Members)
        {
            SyntaxNode? body = member switch
            {
                MethodDeclarationSyntax method => (SyntaxNode?)method.Body ?? method.ExpressionBody,
                ConstructorDeclarationSyntax constructor => (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody,
                _ => null,
            };

            if (body is null)
            {
                continue;
            }

            string name = member is MethodDeclarationSyntax m
                ? m.Identifier.ValueText
                : ((ConstructorDeclarationSyntax)member).Identifier.ValueText;

            methods.Add(new Row(
                fileName,
                declaration.Identifier.ValueText,
                name,
                member.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                CodeLines(body.ToString()),
                Complexity(body),
                Depth(body),
                ((BaseMethodDeclarationSyntax)member).ParameterList.Parameters.Count,
                fanOut));
        }
    }
}

// У таблицю потрапляють лише методи довші за п'ять логічних рядків.
var rows = methods
    .Where(r => r.Loc > 5)
    .OrderByDescending(r => r.Cc)
    .ThenByDescending(r => r.Loc)
    .ThenBy(r => r.Type, StringComparer.Ordinal)
    .ThenBy(r => r.Method, StringComparer.Ordinal)
    .ToList();

var csv = new StringBuilder("File;Class;Method;Line;LOC;CC;Nesting;Params;FanOut\n");
foreach (var r in rows)
{
    csv.Append(r.File).Append(';')
        .Append(r.Type).Append(';')
        .Append(r.Method).Append(';')
        .Append(r.Line.ToString(CultureInfo.InvariantCulture)).Append(';')
        .Append(r.Loc.ToString(CultureInfo.InvariantCulture)).Append(';')
        .Append(r.Cc.ToString(CultureInfo.InvariantCulture)).Append(';')
        .Append(r.Nest.ToString(CultureInfo.InvariantCulture)).Append(';')
        .Append(r.Pars.ToString(CultureInfo.InvariantCulture)).Append(';')
        .Append(r.FanOut.ToString(CultureInfo.InvariantCulture))
        .AppendLine();
}

string? csvDir = Path.GetDirectoryName(Path.GetFullPath(csvPath));
if (!string.IsNullOrEmpty(csvDir))
{
    Directory.CreateDirectory(csvDir);
}

File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(false));

int physical = sources.Values.Sum(PhysicalLines);
int logical = sources.Values.Sum(CodeLines);
var clones = CloneFamilies(sources);

Console.WriteLine($"Файлів: {files.Count}; типів: {projectTypes.Count}; методів: {methods.Count}, у таблиці: {rows.Count}");
Console.WriteLine($"Рядків: {physical} фізичних, {logical} логічних");
Console.WriteLine($"CC: макс. {rows.Max(r => r.Cc)}, середня {rows.Average(r => r.Cc).ToString("0.0", CultureInfo.InvariantCulture)}");
Console.WriteLine($"Найдовший метод: {rows.Max(r => r.Loc)} рядків; вкладеність: {rows.Max(r => r.Nest)}; "
    + $"параметрів: {rows.Max(r => r.Pars)}; fan-out: {rows.Max(r => r.FanOut)}");
Console.WriteLine($"Клонів 6+ рядків: {clones.Count}");
foreach (var clone in clones)
{
    Console.WriteLine($"  {clone.Length} рядків: {string.Join(", ", clone.Places)}");
}
Console.WriteLine($"CSV: {csvPath}");

return 0;

// Логічний рядок: не порожній, не сама дужка і не коментар.
static int CodeLines(string text)
{
    int count = 0;

    foreach (string raw in text.Split('\n'))
    {
        string line = raw.Trim();
        if (line.Length == 0 || line is "{" or "}" or "};")
        {
            continue;
        }

        if (line.StartsWith("//", StringComparison.Ordinal))
        {
            continue;
        }

        count++;
    }

    return count;
}

static int PhysicalLines(string text)
{
    using var reader = new StringReader(text);
    int count = 0;

    while (reader.ReadLine() is not null)
    {
        count++;
    }

    return count;
}

// CC = 1 + розгалуження та логічні зв'язки в тілі методу.
static int Complexity(SyntaxNode body)
{
    int complexity = 1;

    foreach (var node in body.DescendantNodes())
    {
        complexity += node switch
        {
            IfStatementSyntax => 1,
            ForStatementSyntax => 1,
            ForEachStatementSyntax => 1,
            WhileStatementSyntax => 1,
            DoStatementSyntax => 1,
            CatchClauseSyntax => 1,
            CaseSwitchLabelSyntax => 1,
            ConditionalExpressionSyntax => 1,
            ConditionalAccessExpressionSyntax => 1,
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalAndExpression) => 1,
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.LogicalOrExpression) => 1,
            BinaryExpressionSyntax b when b.IsKind(SyntaxKind.CoalesceExpression) => 1,
            AssignmentExpressionSyntax a when a.IsKind(SyntaxKind.CoalesceAssignmentExpression) => 1,
            _ => 0,
        };
    }

    return complexity;
}

// Вкладеність: найбільша кількість керувальних блоків один в одному.
static int Depth(SyntaxNode body)
{
    int max = 1;

    foreach (var node in body.DescendantNodes())
    {
        if (!OpensBlock(node))
        {
            continue;
        }

        int level = 1;
        for (var parent = node.Parent; parent is not null && parent != body; parent = parent.Parent)
        {
            if (OpensBlock(parent))
            {
                level++;
            }
        }

        max = Math.Max(max, level);
    }

    return max;
}

static bool OpensBlock(SyntaxNode node) =>
    node is IfStatementSyntax
        or ForStatementSyntax
        or ForEachStatementSyntax
        or WhileStatementSyntax
        or DoStatementSyntax
        or SwitchStatementSyntax
        or TryStatementSyntax
        or UsingStatementSyntax
        or LockStatementSyntax
        or FixedStatementSyntax;

// Fan-out типу: скільки інших типів проєкту він згадує у своєму тілі.
static int FanOut(SyntaxNode declaration, HashSet<string> projectTypes)
{
    string own = ((BaseTypeDeclarationSyntax)declaration).Identifier.ValueText;

    return declaration
        .DescendantNodes()
        .OfType<IdentifierNameSyntax>()
        .Select(i => i.Identifier.ValueText)
        .Where(name => name != own && projectTypes.Contains(name))
        .Distinct(StringComparer.Ordinal)
        .Count();
}

// Клон — шість і більше однакових нормалізованих рядків у межах файлу.
static List<Clone> CloneFamilies(IReadOnlyDictionary<string, string> sources)
{
    var lines = new List<Line>();

    foreach (var (path, text) in sources.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
    {
        string fileName = Path.GetFileName(path);
        int number = 0;

        foreach (string raw in text.Split('\n'))
        {
            number++;
            string normalized = string.Join(' ', raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (normalized.Length < 3 || normalized is "{" or "}")
            {
                continue;
            }

            if (normalized.StartsWith("using ", StringComparison.Ordinal)
                || normalized.StartsWith("namespace ", StringComparison.Ordinal)
                || normalized.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            lines.Add(new Line(normalized, fileName, number));
        }
    }

    var taken = new HashSet<int>();
    var families = new List<Clone>();

    for (int i = 0; i < lines.Count; i++)
    {
        if (taken.Contains(i))
        {
            continue;
        }

        for (int j = i + 1; j < lines.Count; j++)
        {
            if (lines[i].Text != lines[j].Text)
            {
                continue;
            }

            int length = MatchLength(lines, i, j);
            if (length < 6)
            {
                continue;
            }

            var places = new List<string>
            {
                $"{lines[i].File}:{lines[i].Number}-{lines[i].Number + length - 1}",
                $"{lines[j].File}:{lines[j].Number}-{lines[j].Number + length - 1}",
            };

            for (int k = i; k < i + length; k++)
            {
                taken.Add(k);
            }

            for (int k = j; k < j + length; k++)
            {
                taken.Add(k);
            }

            for (int k = j + 1; k < lines.Count; k++)
            {
                if (lines[k].Text == lines[i].Text && MatchLength(lines, i, k) >= length)
                {
                    places.Add($"{lines[k].File}:{lines[k].Number}-{lines[k].Number + length - 1}");

                    for (int m = k; m < k + length; m++)
                    {
                        taken.Add(m);
                    }
                }
            }

            families.Add(new Clone(length, places));
            break;
        }
    }

    return families
        .Where(f => !families.Any(other =>
            other != f
            && other.Length >= f.Length
            && f.Places.All(other.Places.Contains)))
        .ToList();
}

static int MatchLength(IReadOnlyList<Line> lines, int first, int second)
{
    int length = 0;

    while (first + length < lines.Count
           && second + length < lines.Count
           && lines[first + length].Text == lines[second + length].Text
           && lines[first + length].File == lines[first].File
           && lines[second + length].File == lines[second].File)
    {
        length++;
    }

    return length;
}

record Row(string File, string Type, string Method, int Line, int Loc, int Cc, int Nest, int Pars, int FanOut);

record Line(string Text, string File, int Number);

record Clone(int Length, IReadOnlyList<string> Places);
