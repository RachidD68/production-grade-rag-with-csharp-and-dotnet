// generate-dataset
//
// Produces the synthetic Contoso SmartDocs corpus used throughout the book.
// Deterministic by seed: identical seed -> byte-identical files.
//
// Usage:
//   dotnet run -- [--small] [--large] [--seed 42] [--output ../../data]
//
// Defaults: --small, seed 42, output ../../data (resolved against CWD).
//
// --small    300 documents across 6 silos (HR, Tech, Finance, Legal, Product,
//            Release Notes & Tickets). Fits on a laptop. Phase 0 deliverable.
// --large    5,000 documents — Phase 8 stretch goal. Currently throws
//            NotImplementedException, on purpose.

using RagInDotNet.Tools.GenerateDataset;

var size = DatasetSize.Small;
int seed = 42;
string output = Path.Combine("..", "..", "data");

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--small":
            size = DatasetSize.Small;
            break;
        case "--large":
            size = DatasetSize.Large;
            break;
        case "--seed" when i + 1 < args.Length:
            seed = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--output" when i + 1 < args.Length:
            output = args[++i];
            break;
        case "-h" or "--help":
            PrintHelp();
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            PrintHelp();
            return 2;
    }
}

if (size == DatasetSize.Large)
{
    Console.Error.WriteLine("--large is a Phase 8 stretch goal and is not implemented yet.");
    Console.Error.WriteLine("Run with --small (default) to produce the 300-document corpus.");
    return 1;
}

var outputDir = new DirectoryInfo(output);
return DatasetGenerator.Run(seed, outputDir, size);

static void PrintHelp()
{
    Console.WriteLine("generate-dataset — synthetic Contoso SmartDocs corpus");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --small           Produce the 300-doc dataset (default).");
    Console.WriteLine("  --large           Produce the 5000-doc dataset (Phase 8; not implemented).");
    Console.WriteLine("  --seed <int>      Random seed (default: 42).");
    Console.WriteLine("  --output <path>   Output directory (default: ../../data).");
    Console.WriteLine("  -h, --help        Show this help.");
}
