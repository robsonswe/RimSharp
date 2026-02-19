using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using RimSharp.Infrastructure.Mods.IO;
using Xunit;

namespace RimSharp.Tests.Infrastructure.Mods.IO
{
    public class ModDeletionServiceTests : IDisposable
    {
        private readonly ModDeletionService _service;
        private readonly string _testTempDir;

        public ModDeletionServiceTests()
        {
            _service = new ModDeletionService();
            _testTempDir = Path.Combine(Path.GetTempPath(), "RimSharpTests_Delete_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testTempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testTempDir))
            {
                // Use the service's robust logic to clean up even in case of read-only leftovers
                _service.DeleteDirectoryRobustAsync(_testTempDir).Wait();
            }
        }

        [Fact]
        public async Task DeleteDirectoryRobustAsync_WhenContainsReadOnlyFiles_ShouldDeleteEverything()
        {
            // Arrange
            var subDir = Path.Combine(_testTempDir, "SubDir");
            Directory.CreateDirectory(subDir);
            
            var readOnlyFile = Path.Combine(subDir, "readonly.txt");
            File.WriteAllText(readOnlyFile, "I am read-only");
            File.SetAttributes(readOnlyFile, FileAttributes.ReadOnly);

            // Act
            await _service.DeleteDirectoryRobustAsync(_testTempDir);

            // Assert
            Directory.Exists(_testTempDir).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteDirectoryRobustAsync_WhenContainsReadOnlyFolders_ShouldDeleteEverything()
        {
            // Arrange
            var subDir = Path.Combine(_testTempDir, "ReadOnlyDir");
            Directory.CreateDirectory(subDir);
            File.SetAttributes(subDir, FileAttributes.ReadOnly);
            
            var normalFile = Path.Combine(subDir, "normal.txt");
            File.WriteAllText(normalFile, "I am normal");

            // Act
            await _service.DeleteDirectoryRobustAsync(_testTempDir);

            // Assert
            Directory.Exists(_testTempDir).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteDirectoryRobustAsync_WhenPathDoesNotExist_ShouldNotThrow()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testTempDir, "NonExistent");

            // Act
            Func<Task> act = async () => await _service.DeleteDirectoryRobustAsync(nonExistentPath);

            // Assert
            await act.Should().NotThrowAsync();
        }
    }
}
