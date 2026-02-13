using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// File system tool pack (self-describing + self-registering).
/// </summary>
public sealed class FileSystemToolPack : IToolPack {
    private readonly FileSystemToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="FileSystemToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public FileSystemToolPack(FileSystemToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "fs",
        Name = "File System",
        Tier = ToolCapabilityTier.ReadOnly,
        IsDangerous = false,
        Description = "Safe-by-default file system reads (restricted to AllowedRoots)."
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterFileSystemPack(_options);
    }
}
