using System.Linq;
using System.Data.SqlClient;
using System.Net;
using System.Web.Http;
using 图书馆理模块.Models;
using 图书馆理模块.Services;

namespace 图书馆理模块.Controllers
{
    public class BooksController : ApiController
    {
        private readonly BooksDataContext db = new BooksDataContext();

        // GET /api/Books
        public IHttpActionResult GetBooks()
        {
            var result = (from b in db.BookInfo
                          join c in db.BookCategory on b.CategoryId equals c.CategoryId
                          orderby b.BookNum
                          select new Book
                          {
                              BookNum = b.BookNum,
                              BookName = b.BookName,
                              BookPrice = b.BookPrice ?? 0m,
                              Author = b.Author,
                              Publisher = b.Publisher,
                              CategoryId = b.CategoryId,
                              CategoryName = c.CategoryName
                          }).ToList();
            return Ok(result);
        }

        // GET /api/Books/5
        public IHttpActionResult GetBook(int id)
        {
            if (id <= 0) return BadRequest("无效的图书编号。");
            var result = (from b in db.BookInfo
                          join c in db.BookCategory on b.CategoryId equals c.CategoryId
                          where b.BookNum == id
                          select new Book
                          {
                              BookNum = b.BookNum,
                              BookName = b.BookName,
                              BookPrice = b.BookPrice ?? 0m,
                              Author = b.Author,
                              Publisher = b.Publisher,
                              CategoryId = b.CategoryId,
                              CategoryName = c.CategoryName
                          }).FirstOrDefault();
            return result == null ? (IHttpActionResult)NotFound() : Ok(result);
        }

        // POST /api/Books
        public IHttpActionResult PostBook(Book book)
        {
            if (!LocalAccess.CanWrite()) return StatusCode(HttpStatusCode.Forbidden);
            if (book == null) return BadRequest("图书信息不能为空。");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (!db.BookCategory.Any(c => c.CategoryId == book.CategoryId))
                return BadRequest("请选择有效的图书分类。");

            var entity = new BookInfo
            {
                BookName = book.BookName.Trim(),
                BookPrice = book.BookPrice,
                Author = string.IsNullOrWhiteSpace(book.Author) ? null : book.Author.Trim(),
                Publisher = string.IsNullOrWhiteSpace(book.Publisher) ? null : book.Publisher.Trim(),
                CategoryId = book.CategoryId
            };
            db.BookInfo.InsertOnSubmit(entity);
            db.SubmitChanges();
            book.BookNum = entity.BookNum;
            return CreatedAtRoute("DefaultApi", new { id = entity.BookNum }, book);
        }

        // PUT /api/Books/5
        public IHttpActionResult PutBook(int id, Book book)
        {
            if (!LocalAccess.CanWrite()) return StatusCode(HttpStatusCode.Forbidden);
            if (id <= 0 || book == null) return BadRequest("图书数据无效。");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var entity = db.BookInfo.FirstOrDefault(b => b.BookNum == id);
            if (entity == null) return NotFound();
            if (!db.BookCategory.Any(c => c.CategoryId == book.CategoryId))
                return BadRequest("请选择有效的图书分类。");

            entity.BookName = book.BookName.Trim();
            entity.BookPrice = book.BookPrice;
            entity.Author = string.IsNullOrWhiteSpace(book.Author) ? null : book.Author.Trim();
            entity.Publisher = string.IsNullOrWhiteSpace(book.Publisher) ? null : book.Publisher.Trim();
            entity.CategoryId = book.CategoryId;
            db.SubmitChanges();
            return StatusCode(HttpStatusCode.NoContent);
        }

        // DELETE /api/Books/5
        public IHttpActionResult DeleteBook(int id)
        {
            if (!LocalAccess.CanWrite()) return StatusCode(HttpStatusCode.Forbidden);
            if (id <= 0) return BadRequest("图书编号无效。");
            var entity = db.BookInfo.FirstOrDefault(b => b.BookNum == id);
            if (entity == null) return NotFound();
            db.BookInfo.DeleteOnSubmit(entity);
            try
            {
                db.SubmitChanges();
            }
            catch (SqlException ex)
            {
                if (ex.Number == 547)
                    return Content(HttpStatusCode.Conflict, new { Message = "此书已有实体副本或关联记录，不能删除。请保留历史馆藏信息。" });
                throw;
            }
            return StatusCode(HttpStatusCode.NoContent);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) db.Dispose();
            base.Dispose(disposing);
        }
    }
}
