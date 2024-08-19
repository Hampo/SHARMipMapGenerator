using ImageMagick;
using NetP3DLib.P3D;
using NetP3DLib.P3D.Chunks;
using System.Numerics;
using System.Reflection;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    PrintHelp();
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
}

HashSet<string> valueArguments = [
    "-m",
    "--min_size",
    "-n",
    "--num_mipmaps",
];
List<string> options = [];
Dictionary<string, int> valueOptions = [];
string? inputPath = null;
string? outputPath = null;
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];

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

    if (valueArguments.Contains(arg))
    {
        var valueIndex = ++i;
        if (valueIndex >= args.Length)
        {
            Console.WriteLine($"Not enough arguments for value option \"{arg}\".");
            return;
        }
        string valueStr = args[valueIndex];
        if (!int.TryParse(valueStr, out int value))
        {
            Console.WriteLine($"Value \"{valueStr}\" isn't a number.");
            return;
        }
        valueOptions[arg] = value;
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

int? minimumSize = null;
int? numMipMaps = null;
foreach (var option in valueOptions)
{
    switch (option.Key)
    {
        case "-m":
        case "--min_size":
            if (option.Value < 2 || option.Value > 2048 || !BitOperations.IsPow2(option.Value))
            {
                Console.WriteLine($"Invalid minimum size specified. Please enter a power of 2 between 2 and 2048.");
                return;
            }
            minimumSize = option.Value;
            break;
        case "-n":
        case "--num_mipmaps":
            if (option.Value <= 1)
            {
                Console.WriteLine($"Invalid number of mipmaps specified. Please enter number greater than 1.");
                return;
            }
            numMipMaps = option.Value;
            break;
        default:
            Console.WriteLine($"Unknown/unused value option: {option.Key}={option.Value}");
            break;
    }
}
if (!numMipMaps.HasValue && !minimumSize.HasValue)
{
    Console.WriteLine("You must specify a minimum size and/or a number of mipmaps.");
    PrintHelp();
    Console.WriteLine("Press any key to exit . . .");
    Console.ReadKey(true);
    return;
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
Console.WriteLine($"Minimum Size: {(minimumSize.HasValue ? minimumSize.Value : "NULL")}");
Console.WriteLine($"Number of Mipmaps: {(numMipMaps.HasValue ? numMipMaps.Value : "NULL")}");

try
{
    P3DFile file = new(inputPath);

    var textures = file.GetChunksOfType<TextureChunk>().ToList();
    var sets = file.GetChunksOfType<SetChunk>();
    foreach (var set in sets)
        textures.AddRange(set.GetChunksOfType<TextureChunk>());
    if (!updateAllShaders && textures.Count == 0)
    {
        Console.WriteLine($"Could not find any Texture chunks in file.");
        return;
    }

    bool changed = false;

    var shaders = file.GetChunksOfType<ShaderChunk>();
    if (textures.Count > 0)
    {
        Dictionary<string, List<ShaderChunk>> textureShaderMap = new(textures.Count);
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

        foreach (var texture in textures)
        {
            if (!ValidateTexture(texture))
                continue;

            if (minimumSize.HasValue && (texture.Width < minimumSize || texture.Height < minimumSize))
            {
                Console.WriteLine($"Skipping texture \"{texture.Name}\". Already smaller than minimum size.");
                continue;
            }

            int textureMipMaps;
            if (minimumSize.HasValue)
            {
                textureMipMaps = (int)Math.Log2(Math.Min(texture.Width, texture.Height) / minimumSize.Value) + 1;
                if (numMipMaps.HasValue)
                    textureMipMaps = Math.Min(textureMipMaps, numMipMaps.Value);
            }
            else if (numMipMaps.HasValue)
            {
                textureMipMaps = numMipMaps.Value;
            }
            else
            {
                Console.WriteLine("How are you here? Neither a minimum size of a number of mipmaps specified.");
                return;
            }

            if (texture.NumMipMaps == textureMipMaps)
            {
                Console.WriteLine($"Skipping texture \"{texture.Name}\". Already has {textureMipMaps} mipmaps.");
                continue;
            }

            Console.WriteLine($"Processing: {texture.Name}");

            changed = GenerateMipMaps(texture, textureMipMaps, textureShaderMap);
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
    Console.WriteLine("  -m   | --min_size  <value>     Sets the minimum size mipmap to generate.");
    Console.WriteLine("  -n   | --num_mipmaps  <value>  Sets the number of mipmaps to generate.");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  <input_path>   The input P3D file.");
    Console.WriteLine("  [output_path]  The output P3D file. If omitted, it will attempt to overwrite \"input_path\".");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  SHARMipMapGenerator --min_size 8 C:\\input\\model.p3d C:\\output\\model.p3d");
    Console.WriteLine("  SHARMipMapGenerator --force --no_history --num_mipmaps 3 C:\\input\\model.p3d");
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

static bool ValidateTexture(TextureChunk texture)
{
    if (!BitOperations.IsPow2(texture.Width))
    {
        Console.WriteLine($"Skipping texture \"{texture.Name}\". Width is not a power of 2.");
        return false;
    }

    if (!BitOperations.IsPow2(texture.Height))
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

    return true;
}

static bool GenerateMipMaps(TextureChunk textureChunk, int numMipMaps, Dictionary<string, List<ShaderChunk>> textureShaderMap)
{
    var imageChunk = textureChunk.GetFirstChunkOfType<ImageChunk>();
    var imageDataChunk = imageChunk.GetFirstChunkOfType<ImageDataChunk>();

    List<ImageChunk> images = new(numMipMaps);

    for (int i = 0; i < numMipMaps; i++)
    {
        var width = (int)(imageChunk.Width / Math.Pow(2, i));
        var height = (int)(imageChunk.Height / Math.Pow(2, i));

        var newImage = new ImageChunk($"{imageChunk.Name}_{i}", imageChunk.Version, (uint)width, (uint)height, 32, imageChunk.Palettized, imageChunk.HasAlpha, ImageChunk.Formats.PNG);
        var newImageData = new ImageDataChunk(DownscaleImage(imageDataChunk.ImageData, width, height, textureChunk.Name));
        newImage.Children.Add(newImageData);
        images.Add(newImage);

        if (width == 2 || height == 2)
            break;
    }

    for (int i = textureChunk.Children.Count - 1; i >= 0; i--)
        textureChunk.Children.RemoveAt(i);
    textureChunk.Children.AddRange(images);
    textureChunk.NumMipMaps = (uint)images.Count;
    textureChunk.Bpp = 32;

    if (textureShaderMap.TryGetValue(textureChunk.Name, out var shaderList))
        UpdateShaderFilterMode(shaderList);

    return true;
}

static byte[] DownscaleImage(byte[] imageBytes, int newWidth, int newHeight, string texture)
{
    using var image = new MagickImage(imageBytes);

    image.FilterType = FilterType.Sinc;

    var x = image.Channels.ToArray();
    var channels = image.Separate(Channels.RGBA).ToArray();

    foreach (var channel in channels)
    {
        channel.FilterType = FilterType.Sinc;
        channel.InterpolativeResize(newWidth, newHeight, PixelInterpolateMethod.Spline);
    }

    using var resultImage = new MagickImage(image.BackgroundColor ?? MagickColors.Transparent, newWidth, newHeight);
    resultImage.SetCompression(image.Compression);
    resultImage.Composite(channels[0], CompositeOperator.CopyRed);
    resultImage.Composite(channels[1], CompositeOperator.CopyGreen);
    resultImage.Composite(channels[2], CompositeOperator.CopyBlue);
    if (image.HasAlpha)
    {
        if (channels.Length > 3)
            resultImage.Composite(channels[3], CompositeOperator.CopyAlpha);
        else
        {
            using var alphaChannel = new MagickImage(MagickColors.Black, newWidth, newHeight);
            resultImage.Composite(alphaChannel, CompositeOperator.CopyAlpha);
        }
    }

    resultImage.HasAlpha = image.HasAlpha;
    resultImage.BorderColor = image.BorderColor;
    resultImage.MatteColor = image.MatteColor;
    resultImage.Chromaticity = image.Chromaticity;

    return resultImage.ToByteArray(MagickFormat.Png32);
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