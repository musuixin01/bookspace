using System;
using System.Data.SqlClient;
using System.Net;
using System.Web;
using System.Web.Http;
using 图书馆理模块.Models;
using 图书馆理模块.Services;

namespace 图书馆理模块.Controllers
{
    /// <summary>
    /// Local-only teaching prototype. Production deployment requires
    /// authenticated staff roles, CSRF protection and audit logging.
    /// </summary>
    [RoutePrefix("api/circulation")]
    public sealed class CirculationApiController : ApiController
    {
        private readonly CirculationStore store = new CirculationStore();

        private bool IsAllowed(bool write)
        {
            return write ? LocalAccess.CanWrite() : LocalAccess.CanReadPrivateData();
        }
        private IHttpActionResult Execute(Func<object> action, bool write = false)
        {
            if (!IsAllowed(write))
                return Content(HttpStatusCode.Forbidden,
                    new { Message = "流通业务仅允许本机同源访问。生产部署前请配置管理员登录与权限。" });
            try
            {
                HttpContext.Current.Response.Cache.SetCacheability(HttpCacheability.NoCache);
                HttpContext.Current.Response.Cache.SetNoStore();
                return Ok(action());
            }
            catch (CirculationException ex)
            {
                return Content(ex.Status, new { Message = ex.Message });
            }
            catch (SqlException ex)
            {
                if (ex.Number == 2601 || ex.Number == 2627)
                    return Content(HttpStatusCode.Conflict, new { Message = "副本编号或读者证号已存在，或此书已被借出。" });
                if (ex.Number == 547)
                    return Content(HttpStatusCode.Conflict, new { Message = "关联图书或读者不存在，或操作违反数据库约束。" });
                if (ex.Number == 1205)
                    return Content(HttpStatusCode.Conflict, new { Message = "其他操作正在占用该记录，请刷新后重试。" });
                if (ex.Number == 208 || ex.Number == 207)
                    return Content(HttpStatusCode.ServiceUnavailable,
                        new { Message = "缺少流通业务数据表，请检查基础表及 CIRCULATION_UPGRADE_V2.sql 是否已人工执行。" });
                return Content(HttpStatusCode.ServiceUnavailable,
                    new { Message = "数据库暂时不可用。请检查连接、权限及流通表结构。" });
            }
        }

        [HttpGet, Route("copies")]
        public IHttpActionResult GetCopies() { return Execute(() => store.Copies()); }

        [HttpGet, Route("readers")]
        public IHttpActionResult GetReaders() { return Execute(() => store.Readers()); }

        [HttpGet, Route("loans")]
        public IHttpActionResult GetLoans() { return Execute(() => store.Loans()); }

        [HttpPost, Route("copies")]
        public IHttpActionResult AddCopy(NewCopyRequest input)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            return Execute(() => new { Id = store.RegisterCopy(input) }, true);
        }

        [HttpPost, Route("readers")]
        public IHttpActionResult AddReader(NewReaderRequest input)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            return Execute(() => new { Id = store.RegisterReader(input) }, true);
        }

        [HttpPost, Route("loans")]
        public IHttpActionResult Borrow(BorrowRequest input)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            return Execute(() => new { Id = store.Borrow(input), Days = CirculationRules.LoanDays }, true);
        }

        [HttpPost, Route("loans/{loanId:int}/return")]
        public IHttpActionResult Return(int loanId)
        {
            return Execute(() => { store.Return(loanId); return new { Returned = true }; }, true);
        }
        [HttpGet, Route("loans/overdue")]
        public IHttpActionResult GetOverdueLoans() { return Execute(() => store.Loans(true)); }

        [HttpGet, Route("reservations")]
        public IHttpActionResult GetReservations() { return Execute(() => store.Reservations()); }

        [HttpPut, Route("readers/{readerId:int}")]
        public IHttpActionResult UpdateReader(int readerId, UpdateReaderRequest input)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            return Execute(() => { store.UpdateReader(readerId, input); return new { Updated = true }; }, true);
        }

        [HttpPost, Route("readers/{readerId:int}/status")]
        public IHttpActionResult SetReaderStatus(int readerId, ReaderStatusRequest input)
        {
            if (input == null || !ModelState.IsValid) return BadRequest(ModelState);
            return Execute(() => { store.SetReaderStatus(readerId, input.IsActive); return new { Updated = true }; }, true);
        }

        [HttpPost, Route("loans/{loanId:int}/renew")]
        public IHttpActionResult RenewLoan(int loanId)
        {
            return Execute(() => new { DueAtUtc = store.Renew(loanId) }, true);
        }

        [HttpPost, Route("reservations")]
        public IHttpActionResult Reserve(NewReservationRequest input)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            return Execute(() => new { Id = store.Reserve(input) }, true);
        }

        [HttpPost, Route("reservations/{reservationId:int}/cancel")]
        public IHttpActionResult CancelReservation(int reservationId)
        {
            return Execute(() =>
            {
                store.CancelReservation(reservationId);
                return new { Canceled = true };
            }, true);
        }

    }
}