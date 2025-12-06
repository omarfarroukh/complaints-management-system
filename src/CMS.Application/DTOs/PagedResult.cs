// CMS.Application/DTOs/PagedResult.cs
namespace CMS.Application.DTOs
{
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
    }
}