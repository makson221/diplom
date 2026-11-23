using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorksArchive.Data;

namespace WorksArchive.Pages.Admin;

public class UsersModel(AppDbContext db, ILogger<UsersModel> log) : PageModel
{
    public class NewUserInput
    {
        [Required(ErrorMessage = "Вкажіть ПІБ"), StringLength(150), Display(Name = "ПІБ")]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Вкажіть логін"), StringLength(64, MinimumLength = 3, ErrorMessage = "Логін: 3–64 символи"),
         RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Логін: лише латиниця, цифри, . _ -"), Display(Name = "Логін")]
        public string Login { get; set; } = "";

        [Required(ErrorMessage = "Вкажіть пароль"), MinLength(6, ErrorMessage = "Пароль: мінімум 6 символів"),
         DataType(DataType.Password), Display(Name = "Пароль")]
        public string Password { get; set; } = "";

        [Display(Name = "Роль")]
        public UserRole Role { get; set; } = UserRole.Teacher;
    }

    [BindProperty] public NewUserInput NewUser { get; set; } = new();

    public List<AppUser> Users { get; private set; } = new();
    public int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task LoadAsync() =>
        Users = await db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.FullName).ToListAsync();

    public Task OnGetAsync() => LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var login = NewUser.Login.Trim();
        if (await db.Users.AnyAsync(u => u.Login == login))
            ModelState.AddModelError("NewUser.Login", "Такий логін уже існує");
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var user = new AppUser { Login = login, FullName = NewUser.FullName.Trim(), Role = NewUser.Role };
        user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, NewUser.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        log.LogInformation("{Admin} створив користувача {Login} ({Role})", User.Identity?.Name, login, user.Role);
        TempData["Success"] = $"Користувача «{user.FullName}» створено. Логін: {login}";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, string fullName, UserRole? role, string? newPassword)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            var name = fullName.Trim();
            user.FullName = name.Length > 150 ? name[..150] : name;
        }

        var invalidate = false;
        if (role.HasValue && id != CurrentUserId && role != user.Role)
        {
            user.Role = role.Value;
            invalidate = true;
        }
        if (!string.IsNullOrEmpty(newPassword))
        {
            if (newPassword.Length < 6)
            {
                TempData["Error"] = "Пароль: мінімум 6 символів";
                return RedirectToPage();
            }
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(user, newPassword);
            invalidate |= id != CurrentUserId;
        }
        if (invalidate) user.SecurityStamp = Guid.NewGuid().ToString("N");

        await db.SaveChangesAsync();
        TempData["Success"] = "Зміни збережено";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(int id)
    {
        if (id == CurrentUserId) return BadRequest();
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.IsActive = !user.IsActive;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync();
        TempData["Success"] = user.IsActive ? $"«{user.FullName}» розблоковано" : $"«{user.FullName}» заблоковано";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (id == CurrentUserId) return BadRequest();
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Role == UserRole.Admin && await db.Users.CountAsync(u => u.Role == UserRole.Admin) <= 1)
        {
            TempData["Error"] = "Не можна видалити останнього адміністратора";
            return RedirectToPage();
        }
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        log.LogInformation("{Admin} видалив користувача {Login}", User.Identity?.Name, user.Login);
        TempData["Success"] = $"Користувача «{user.FullName}» видалено";
        return RedirectToPage();
    }
}
