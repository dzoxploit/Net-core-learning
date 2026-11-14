using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DeretBilangan.Models;
using DeretBilangan.Services;

namespace DeretBilangan.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// GET form: the calculation changes nothing, so the inputs live in the URL
    /// (/?JumlahDeret=10&amp;AngkaDicari=6) and a result can be bookmarked or shared.
    /// </summary>
    public IActionResult Index([FromQuery] DeretInput input)
    {
        var vm = new DeretVm { Input = input };
        var adaInput = Request.Query.ContainsKey(nameof(DeretInput.JumlahDeret));

        if (!adaInput)
        {
            ModelState.Clear(); // first visit: empty form, no "required" errors yet
            vm.Input = new DeretInput { JumlahDeret = 10, AngkaDicari = 6 };
        }
        else if (ModelState.IsValid)
        {
            vm.Hasil = Deret.Hitung(input.JumlahDeret!.Value, input.AngkaDicari);
        }
        return View(vm);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
