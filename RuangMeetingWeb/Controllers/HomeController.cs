using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RuangMeetingWeb.Models;

namespace RuangMeetingWeb.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Reservasi");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
