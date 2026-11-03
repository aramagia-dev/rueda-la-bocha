using Microsoft.EntityFrameworkCore;
using RuedaLaBocha.Web.Data;
using RuedaLaBocha.Web.Models;

namespace RuedaLaBocha.Web.Services;

public sealed class CourtService(ApplicationDbContext db)
{
    public Task<List<Court>> ListAsync(bool includeInactive = false) => db.Courts.AsNoTracking()
        .Where(c => includeInactive || c.IsActive).OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync();

    public Task<Court?> FindAsync(int id) => db.Courts.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);

    public async Task CreateAsync(CourtForm form)
    {
        db.Courts.Add(new Court { Name = form.Name.Trim(), Description = form.Description?.Trim() ?? "", IsActive = form.IsActive });
        await db.SaveChangesAsync();
    }

    public async Task<bool> UpdateAsync(int id, CourtForm form)
    {
        var court = await db.Courts.FindAsync(id);
        if (court is null) return false;
        court.Name = form.Name.Trim();
        court.Description = form.Description?.Trim() ?? "";
        court.IsActive = form.IsActive;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        var court = await db.Courts.FindAsync(id);
        if (court is null) return false;
        court.IsActive = false;
        await db.SaveChangesAsync();
        return true;
    }
}
