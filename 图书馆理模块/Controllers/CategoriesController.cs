using System.Linq;
using System.Web.Http;
using 图书馆理模块.Models;

namespace 图书馆理模块.Controllers
{
    public class CategoriesController : ApiController
    {
        private readonly BooksDataContext db = new BooksDataContext();

        // GET /api/Categories
        public IHttpActionResult GetCategories()
        {
            var categories = db.BookCategory.OrderBy(c => c.CategoryId)
                .Select(c => new Category {
                    CategoryId = c.CategoryId,
                    CategoryName = c.CategoryName
                }).ToList();
            return Ok(categories);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
