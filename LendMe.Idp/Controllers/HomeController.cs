using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using LendMe.Idp.Models;
using Microsoft.AspNetCore.Authorization;

namespace LendMe.Idp.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [Authorize]
    public IActionResult Privacy()
    {
        return View();
    }

    public IActionResult Error()
    {
        return View();
    }
}