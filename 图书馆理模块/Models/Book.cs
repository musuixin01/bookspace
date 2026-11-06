using System.ComponentModel.DataAnnotations;

namespace 图书馆理模块.Models
{
    public class Book
    {
        public int BookNum { get; set; }

        [Required(ErrorMessage = "请输入书名。")]
        [StringLength(50, ErrorMessage = "书名最多 50 个字符。")]
        public string BookName { get; set; }

        [Range(typeof(decimal), "0", "9999.99", ErrorMessage = "价格应在 0 至 9999.99 元之间。")]
        public decimal BookPrice { get; set; }

        [StringLength(30, ErrorMessage = "作者最多 30 个字符。")]
        public string Author { get; set; }

        [StringLength(100, ErrorMessage = "出版社最多 100 个字符。")]
        public string Publisher { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "请选择图书分类。")]
        public int CategoryId { get; set; }

        public string CategoryName { get; set; }
    }
}
