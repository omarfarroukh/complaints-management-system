using System.IO;

namespace CMS.Application.Interfaces
{
    public interface IFileStorageService
    {
        Task<string> SaveFileAsync(Stream fileStream, string fileName, string folderName);
        Task DeleteFileAsync(string filePath);
    }
}
