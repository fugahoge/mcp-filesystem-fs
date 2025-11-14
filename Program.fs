namespace McpFilesystemFs

open System
open System.ComponentModel
open System.IO
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open ModelContextProtocol.Server

module ProgramState =
    let mutable RootDirectory : string = String.Empty

[<McpServerToolType>]
type FilesystemTool() =
    static member private SanitizeFilename(filename: string) =
        let normalized = filename.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
        let justName = Path.GetFileName(normalized) |> Option.ofObj
        match justName with
        | Some name when not (String.IsNullOrEmpty(name)) -> name
        | _ -> filename

    [<McpServerTool; Description("Read the complete contents of a file from the directory as text. " +
          "Handles various text encodings and provides detailed error messages if the file cannot be read. " +
          "Use this tool when you need to examine the contents of a single file. " +
          "Use the 'head' parameter to read only the first N lines of a file, or the 'tail' parameter to read only the last N lines of a file. " +
          "Operates on the file as text regardless of extension. " +
          "File must be in the directory specified at startup.")>]
    member _.ReadTextFile([<Description("The filename to read from the directory")>] filename: string,
                          [<Description("If provided, returns only the first N lines of the file")>] head: Nullable<int>,
                          [<Description("If provided, returns only the last N lines of the file")>] tail: Nullable<int>) : string =
        try
            let sanitized = FilesystemTool.SanitizeFilename(filename)
            let fullPath = Path.Combine(ProgramState.RootDirectory, sanitized)
            if not (File.Exists(fullPath)) then
                raise (FileNotFoundException($"Error: File '{filename}' not found in directory."))
            
            let content = File.ReadAllText(fullPath)
            let mutable lines = content.Split('\n')
            if head.HasValue then
                lines <- lines |> Seq.truncate head.Value |> Seq.toArray
            elif tail.HasValue then
                lines <- lines |> Seq.rev |> Seq.truncate tail.Value |> Seq.rev |> Seq.toArray
            String.Join("\n", lines)
        with ex ->
            $"Error reading file: {ex.Message}"

    [<McpServerTool; Description("Create or overwrite a text file with specific content at the given path inside the startup directory. " +
            "This command is used to write generated results or other output into a file, such as 'output.txt'. " +
            "If the file already exists, its entire content will be replaced with the new data. " +
            "Use with caution, as it will overwrite without confirmation. " +
            "All text is saved with proper encoding.")>]
    member _.WriteFile([<Description("The filename to write in the directory")>] filename: string,
                       [<Description("The content to write to the file")>] content: string) : string =
        try
            let sanitized = FilesystemTool.SanitizeFilename(filename)
            let fullPath = Path.Combine(ProgramState.RootDirectory, sanitized)
            File.WriteAllText(fullPath, content)
            $"Successfully wrote to {filename}"
        with ex ->
            $"Error writing file: {ex.Message}"

    [<McpServerTool; Description("Get a detailed listing of all files in the directory. " +
          "Returns only file names, excluding directories. " +
          "This tool is essential for finding specific files within the directory.")>]
    member _.ListFiles() : string =
        try
            let entries = Directory.GetFileSystemEntries(ProgramState.RootDirectory)
            let filesOnly =
                entries
                |> Array.filter (fun p -> not (Directory.Exists(p)))
                |> Array.map Path.GetFileName
            if filesOnly.Length > 0 then String.Join("\n", filesOnly) else "No files found"
        with ex ->
            $"Error listing files: {ex.Message}"

    [<McpServerTool; Description("Recursively search for files and directories matching a pattern in the directory. " +
          "The patterns should be glob-style patterns that match files. " +
          "Use pattern like '*.ext' to match files in directory, and '**/*.ext' to match files in all subdirectories. " +
          "Returns filenames of all matching items. " +
          "Great for finding files when you don't know their exact location. " +
          "Searches only within the directory specified at startup.")>]
    member _.SearchFiles([<Description("The glob pattern to match files against")>] pattern: string) : string =
        try
            let files = Directory.GetFiles(ProgramState.RootDirectory, pattern, SearchOption.AllDirectories)
            let names = files |> Array.map Path.GetFileName
            if names.Length > 0 then String.Join("\n", names) else "No matches found"
        with ex ->
            $"Error searching files: {ex.Message}"

    [<McpServerTool; Description("Retrieve detailed metadata about a file in the directory. " +
          "Returns comprehensive information including size, creation time, last modified time, permissions, and type. " +
          "This tool is perfect for understanding file characteristics without reading the actual content. " +
          "File must be in the directory specified at startup.")>]
    member _.GetFileInfo([<Description("The filename name in the directory")>] name: string) : string =
        try
            let sanitized = FilesystemTool.SanitizeFilename(name)
            let fullPath = Path.Combine(ProgramState.RootDirectory, sanitized)
            if (not (File.Exists(fullPath))) && (not (Directory.Exists(fullPath))) then
                raise (FileNotFoundException($"Error: Item '{name}' not found in directory."))
            
            let info = FileInfo(fullPath)
            let parts =
                [| $"Name: {info.Name}"
                   $"FullName: {info.FullName}"
                   $"Length: {info.Length} bytes"
                   $"CreationTime: {info.CreationTime}"
                   $"LastWriteTime: {info.LastWriteTime}"
                   $"LastAccessTime: {info.LastAccessTime}"
                   $"Attributes: {info.Attributes}"
                   $"Extension: {info.Extension}"
                   $"DirectoryName: {info.DirectoryName}" |]
            String.Join("\n", parts)
        with ex ->
            $"Error getting file info: {ex.Message}"

module Program =
    [<EntryPoint>]
    let main argv =
        if argv.Length = 0 then
            eprintfn "Error: directory argument is required."
            eprintfn "Usage: McpFilesystem <directory>"
            Environment.Exit(1)

        let rootDir = argv[0]
        if not (Directory.Exists(rootDir)) then
            eprintfn $"Error: Root directory '{rootDir}' does not exist."
            Environment.Exit(1)

        ProgramState.RootDirectory <- Path.GetFullPath(rootDir)
        eprintfn $"Root directory set to: {ProgramState.RootDirectory}"

        let builder = Host.CreateApplicationBuilder(argv)
        builder.Logging.AddConsole(fun o -> o.LogToStandardErrorThreshold <- LogLevel.Trace) |> ignore

        builder.Services
            .AddMcpServer(fun options ->
                options.ServerInfo <- ModelContextProtocol.Protocol.Implementation(Name = "mcpFilesystem", Version = "1.0.0")
            )
            .WithStdioServerTransport()
            .WithTools<FilesystemTool>()
        |> ignore

        builder.Build().Run()
        0


