using System.Web.Mvc;

namespace 图书馆理模块.Controllers
{
    public sealed class CirculationController : Controller
    {
        public ActionResult Index()
        {
            // The existing course site has no authentication system.
            // Keep the new personal-data workbench restricted to localhost.
            if (!Request.IsLocal) return new HttpStatusCodeResult(403);
            return View();
        }
    }
}