using Imageflow.Fluent;
using NetP3DLib.P3D;
using NetP3DLib.P3D.Chunks;
using System.Reflection;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}

List<string> options = [];
string? inputPath = null;
string? outputPath = null;
foreach (string arg in args)
{
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        Console.WriteLine($"Unknown/unused argument: {arg}");
        continue;
    }

    if (!string.IsNullOrWhiteSpace(inputPath))
    {
        outputPath = arg;
        continue;
    }

    if (arg.StartsWith('-'))
    {
        options.Add(arg[1..]);
        continue;
    }

    inputPath = arg;
}

if (string.IsNullOrWhiteSpace(inputPath))
{
    Console.WriteLine("No input path specified.");
    PrintHelp();
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}

bool force = false;
bool noHistory = false;
bool updateAllShaders = false;
foreach (var option in options)
{
    switch (option)
    {
        case "f":
        case "-force":
            force = true;
            break;
        case "nh":
        case "-no_history":
            noHistory = true;
            break;
        case "uas":
        case "-update_all_shaders":
            updateAllShaders = true;
            break;
        default:
            Console.WriteLine($"Unknown/unused option: {option}");
            break;
    }
}

var inputFileInfo = new FileInfo(inputPath);
if (!inputFileInfo.Exists)
{
    Console.WriteLine($"Could not find input path: {inputPath}");
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}
inputPath = inputFileInfo.FullName;

if (string.IsNullOrWhiteSpace(outputPath))
{
    outputPath = inputPath;
}

var outputFileInfo = new FileInfo(outputPath);
if (!IsValidOutputPath(outputFileInfo.FullName))
{
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}
if (outputFileInfo.Exists && outputFileInfo.IsReadOnly)
{
    Console.WriteLine($"Output path \"{outputFileInfo.FullName}\" is read only.");
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}
if (outputFileInfo.Exists && !force)
{
    string? response;
    do
    {
        Console.WriteLine($"Output file \"{outputFileInfo.FullName}\" already exists. Do you want to overwrite? [Yes/No]");
        response = Console.ReadLine();
        if (response != null && response.Equals("no", StringComparison.OrdinalIgnoreCase))
            return;
    } while (response?.ToLower() != "yes");
}
outputPath = outputFileInfo.FullName;

if (!Path.GetExtension(inputPath).Equals(".p3d", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Input must be a P3D file.");
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}

if (!Path.GetExtension(outputPath).Equals(".p3d", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Output must be a P3D file.");
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}

Console.WriteLine($"Input Path: {inputPath}.");
Console.WriteLine($"Output Path: {outputPath}.");
Console.WriteLine($"Force: {force}.");
Console.WriteLine($"No History: {noHistory}.");
Console.WriteLine($"Update All Shaders: {updateAllShaders}.");

var modes = Enum.GetValues<Mode>();
Mode mode;
while (true)
{
    Console.WriteLine("Pick a generation mode:");
    for (int i = 0; i < modes.Length; i++)
        Console.WriteLine($"\t[{i}] {modes[i]}");

    string? indexStr = Console.ReadLine();
    if (!int.TryParse(indexStr, out var index) || index < 0 || index >= modes.Length)
    {
        Console.WriteLine($"Invalid index specified. Please enter an index between 0 and {modes.Length - 1}.");
        continue;
    }

    mode = modes[index];
    break;
}
Console.WriteLine($"Using mode: {mode}");

try
{
    P3DFile file = new(inputPath);

    var textures = file.GetChunksOfType<TextureChunk>();
    if (!updateAllShaders && textures.Length == 0)
    {
        Console.WriteLine($"Could not find any Texture chunks in file.");
        return;
    }

    bool changed = false;

    var shaders = file.GetChunksOfType<ShaderChunk>();
    if (textures.Length > 0)
    {
        Dictionary<string, List<ShaderChunk>> textureShaderMap = new(textures.Length);
        foreach (var shader in shaders)
        {
            var textureParam = shader.GetLastParamOfType<ShaderTextureParameterChunk>("TEX");
            if (textureParam == null)
                continue;
            if (!textureShaderMap.TryGetValue(textureParam.Value, out var textureShaderList))
            {
                textureShaderList = [];
                textureShaderMap[textureParam.Value] = textureShaderList;
            }
            textureShaderList.Add(shader);
        }
        switch (mode)
        {
            case Mode.Number_of_MipMaps:
                {
                    int numMipMaps;
                    while (true)
                    {
                        Console.WriteLine("Enter number of mipmaps:");

                        string? numStr = Console.ReadLine();
                        if (!int.TryParse(numStr, out var num) || num <= 1)
                        {
                            Console.WriteLine($"Invalid number specified. Please enter number greater than 1.");
                            continue;
                        }

                        numMipMaps = num;
                        break;
                    }

                    foreach (var texture in textures)
                    {
                        if (texture.NumMipMaps == numMipMaps)
                        {
                            Console.WriteLine($"Skipping texture \"{texture.Name}\". Already has {numMipMaps} mipmaps.");
                            continue;
                        }

                        if (!await ValidateTexture(texture))
                            continue;

                        Console.WriteLine($"Processing: {texture.Name}");

                        changed = await GenerateMipMaps(texture, numMipMaps, textureShaderMap);
                    }
                    break;
                }
            case Mode.Minimum_Size:
                {
                    int minimumSize;
                    while (true)
                    {
                        Console.WriteLine("Enter minimum size:");

                        string? numStr = Console.ReadLine();
                        if (!int.TryParse(numStr, out var num) || num % 2 != 0 || num < 2 || num > 2048)
                        {
                            Console.WriteLine($"Invalid number specified. Please enter number greater than or equal to 2. It must also be a power of 2");
                            continue;
                        }

                        minimumSize = num;
                        break;
                    }

                    foreach (var texture in textures)
                    {
                        if (texture.Width < minimumSize || texture.Height < minimumSize)
                        {
                            Console.WriteLine($"Skipping texture \"{texture.Name}\". Already smaller than minimum size.");
                            continue;
                        }

                        if (!await ValidateTexture(texture))
                            continue;

                        Console.WriteLine($"Processing: {texture.Name}");

                        int numMipMaps = (int)Math.Log2(Math.Min(texture.Width, texture.Height) / minimumSize) + 1;

                        changed = await GenerateMipMaps(texture, numMipMaps, textureShaderMap);
                    }
                    break;
                }
            default:
                Console.WriteLine($"Unimplemented mode: {mode}");
                return;
        }
    }

    if (updateAllShaders)
    {
        changed = UpdateShaderFilterMode(shaders) || changed;
    }

    if (!changed)
    {
        Console.WriteLine("No changes were made. Exiting.");
        return;
    }

    if (!noHistory)
        file.Chunks.Insert(0, new HistoryChunk([$"MipMaps Generated by SHARMipMapGenerator v{Assembly.GetExecutingAssembly().GetName().Version}.", $"SHARMipMapGenerator \"{string.Join("\" \"", args)}\"", $"Run at {DateTime.Now:R}"]));

    file.Write(outputPath);

    Console.WriteLine($"Saved updated P3D file to: {outputPath}");
}
catch (Exception ex)
{
    Console.WriteLine($"There was an error generating mipmaps: {ex}");
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
}

static void PrintHelp()
{
    Console.WriteLine("Usage: SHARMipMapGenerator [options] <input_path> [output_path]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  -f   | --force                 Force overwrite the output file.");
    Console.WriteLine("  -nh  | --no_history            Don't add history chunk.");
    Console.WriteLine("  -uas | --update_all_shaders    Updates all shaders in the file to set their filter mode to use mipmaps.");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  <input_path>   The input P3D file.");
    Console.WriteLine("  [output_path]  The output P3D file. If omitted, it will attempt to overwrite \"input_path\".");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  SHARMipMapGenerator C:\\input\\model.p3d C:\\output\\model.p3d");
    Console.WriteLine("  SHARMipMapGenerator --force --no_history C:\\input\\model.p3d");
    Console.WriteLine();
}

static bool IsValidOutputPath(string outputPath)
{
    if (outputPath.IndexOfAny(Path.GetInvalidPathChars()) != -1)
    {
        Console.WriteLine($"Output path \"{outputPath}\" contains invalid characters.");
        return false;
    }

    var directory = Path.GetDirectoryName(outputPath);
    if (!Directory.Exists(directory))
    {
        Console.WriteLine($"Output directory \"{(string.IsNullOrWhiteSpace(directory), Environment.CurrentDirectory, directory)}\" doesn't exist.");
        return false;
    }

    try
    {
        var path = Path.GetRandomFileName();
        if (!string.IsNullOrWhiteSpace(directory))
            path = Path.Combine(directory, path);
        using FileStream fs = File.Create(path, 1, FileOptions.DeleteOnClose);
    }
    catch
    {
        return false;
    }

    return true;
}

static async Task<bool> ValidateTexture(TextureChunk texture)
{
    if (texture.Width % 2 != 0)
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". Width is not a power of 2.");
        return false;
    }

    if (texture.Height % 2 != 0)
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". Height is not a power of 2.");
        return false;
    }

    var imageChunks = texture.GetChunksOfType<ImageChunk>();
    if (imageChunks.Length != texture.NumMipMaps)
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". Number of image children does not match current number of mipmaps.");
        return false;
    }

    if (texture.NumMipMaps == 0)
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". No image children.");
        return false;
    }

    var imageChunk = imageChunks[0];
    var imageDataChunks = imageChunk.GetChunksOfType<ImageDataChunk>();
    if (imageDataChunks.Length != 1)
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". Image has more than one image data chunk.");
        return false;
    }

    if (texture.Width != imageChunk.Width || texture.Height != imageChunk.Height)
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". Image dimensions do not match texture dimensions.");
        return false;
    }

    var imageInfo = await ImageJob.GetImageInfoAsync(new MemorySource(imageDataChunks[0].ImageData), SourceLifetime.NowOwnedAndDisposedByTask);
    if (imageInfo.ImageWidth != texture.Width || imageInfo.ImageHeight != texture.Height)
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". Image data dimensions do not match texture dimensions.");
        return false;
    }

    return true;
}

static async Task<bool> GenerateMipMaps(TextureChunk textureChunk, int numMipMaps, Dictionary<string, List<ShaderChunk>> textureShaderMap)
{
    var imageChunk = textureChunk.GetFirstChunkOfType<ImageChunk>();
    var imageDataChunk = imageChunk.GetFirstChunkOfType<ImageDataChunk>();

    using var imageJob = new ImageJob();
    var node = imageJob.Decode(imageDataChunk.ImageData)
        .Constrain(new(ConstraintMode.Fit, imageChunk.Width, imageChunk.Height));
    for (int i = 1; i < numMipMaps; i++)
        node.Branch(f => f.Constrain(new(ConstraintMode.Fit, (uint)(imageChunk.Width / Math.Pow(2, i)), (uint)(imageChunk.Height / Math.Pow(2, i)))).EncodeToBytes(new LodePngEncoder()));
    var result = await node.EncodeToBytes(new LodePngEncoder()).Finish().InProcessAndDisposeAsync();

    List<ImageChunk> images = new(numMipMaps);
    for (int i = 1; i < numMipMaps; i++)
    {
        var imageResult = result.TryGet(i);
        if (imageResult == null)
        {
            Console.WriteLine($"Skipping texture \"{textureChunk.Name}\". Error resizing image.");
            return false;
        }
        var newImage = new ImageChunk($"{imageChunk.Name}_{i}", imageChunk.Version, (uint)imageResult.Width, (uint)imageResult.Height, imageChunk.Bpp, imageChunk.Palettized, imageChunk.HasAlpha, ImageChunk.Formats.PNG);
        var newImageData = new ImageDataChunk(imageResult.TryGetBytes()?.ToArray());
        newImage.Children.Add(newImageData);
        images.Add(newImage);
    }
    {
        var imageResult = result.TryGet(numMipMaps);
        if (imageResult == null)
        {
            Console.WriteLine($"Skipping texture \"{textureChunk.Name}\". Error resizing image.");
            return false;
        }
        var newImage = new ImageChunk($"{imageChunk.Name}", imageChunk.Version, (uint)imageResult.Width, (uint)imageResult.Height, imageChunk.Bpp, imageChunk.Palettized, imageChunk.HasAlpha, ImageChunk.Formats.PNG);
        var newImageData = new ImageDataChunk(imageResult.TryGetBytes()?.ToArray());
        newImage.Children.Add(newImageData);
        images.Insert(0, newImage);
    }

    for (int i = textureChunk.Children.Count - 1; i >= 0; i--)
        textureChunk.Children.RemoveAt(i);
    textureChunk.Children.AddRange(images);
    textureChunk.NumMipMaps = (uint)numMipMaps;

    if (textureShaderMap.TryGetValue(textureChunk.Name, out var shaderList))
        UpdateShaderFilterMode(shaderList);

    return true;
}

static bool UpdateShaderFilterMode(IList<ShaderChunk> shaderList)
{
    bool changed = false;
    foreach (var shader in shaderList)
    {
        var filterModeParam = shader.GetLastParamOfType<ShaderIntegerParameterChunk>("FIMD");
        switch (filterModeParam.Value)
        {
            case 0:
                filterModeParam.Value = 2;
                changed = true;
                break;
            case 1:
                filterModeParam.Value = 4;
                changed = true;
                break;
        }
    }
    return changed;
}

enum Mode
{
    Number_of_MipMaps,
    Minimum_Size
}