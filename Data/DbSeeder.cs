using Microsoft.AspNetCore.Identity;
using Nook.Api.Models;

namespace Nook.Api.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        // Idempotent: only seed when the database doesn't already look like a
        // fully-seeded Rev 2 database (5 offices). Program.cs is responsible for
        // making sure the schema itself (tables/columns) is Rev 2 shaped before
        // this runs; if it detected a stale Rev 1 (Floor-based) schema it drops
        // and recreates the tables, so an empty/mismatched Offices table here
        // means we need a full reseed.
        if (db.Users.Any() && db.Offices.Count() == 5) return;

        // Clear out any partial/stale data before reseeding (safe: pre-production
        // seed/demo data only).
        db.Bookings.RemoveRange(db.Bookings);
        db.Workspaces.RemoveRange(db.Workspaces);
        db.Offices.RemoveRange(db.Offices);
        db.Users.RemoveRange(db.Users);
        db.SaveChanges();

        // ---- Offices ----
        var studio = new Office { Name = "The Studio", Ordinal = 0 };
        var workshop = new Office { Name = "The Workshop", Ordinal = 1 };
        var library = new Office { Name = "The Library", Ordinal = 2 };
        var corner = new Office { Name = "The Corner", Ordinal = 3 };
        var greenhouse = new Office { Name = "The Greenhouse", Ordinal = 4 };
        db.Offices.AddRange(studio, workshop, library, corner, greenhouse);

        // ---- Desks (3 per office, 15 total) ----
        string[] deskAmenities =
        [
            "Monitor",
            "Monitor,Window",
            "Standing",
            "Monitor,Standing",
            "Window",
            "Quiet",
            "Monitor,Window,Standing",
            "Standing,Window",
            "Dual monitor",
            "Dual monitor,Standing",
            "Quiet,Window",
            "Monitor,Quiet",
            "Standing,Quiet",
            "Dual monitor,Window",
            "Monitor,Standing,Window",
        ];

        Workspace Desk(Office office, int number) => new()
        {
            Office = office,
            Name = $"Desk {number}",
            Type = WorkspaceTypes.Desk,
            Capacity = 1,
            Amenities = deskAmenities[(number - 1) % deskAmenities.Length],
        };

        var workspaces = new List<Workspace>();
        for (var i = 1; i <= 3; i++) workspaces.Add(Desk(studio, i));      // Desks 1-3
        for (var i = 4; i <= 6; i++) workspaces.Add(Desk(workshop, i));    // Desks 4-6
        for (var i = 7; i <= 9; i++) workspaces.Add(Desk(library, i));     // Desks 7-9
        for (var i = 10; i <= 12; i++) workspaces.Add(Desk(corner, i));    // Desks 10-12
        for (var i = 13; i <= 15; i++) workspaces.Add(Desk(greenhouse, i)); // Desks 13-15

        var desk2 = workspaces.First(w => w.Name == "Desk 2");
        var desk7 = workspaces.First(w => w.Name == "Desk 7");
        var desk11 = workspaces.First(w => w.Name == "Desk 11");
        db.Workspaces.AddRange(workspaces);

        // ---- Users (all passwords: Password123!) ----
        var hasher = new PasswordHasher<User>();

        User MakeUser(string fullName, string email, string role, string department)
        {
            var user = new User
            {
                FullName = fullName,
                Email = email,
                Role = role,
                Department = department,
            };
            user.PasswordHash = hasher.HashPassword(user, "Password123!");
            return user;
        }

        var ama = MakeUser("Ama Owusu", "ama@demo.com", Roles.Employee, "Design");
        var kwame = MakeUser("Kwame Mensah", "kwame@demo.com", Roles.Employee, "Engineering");
        var esi = MakeUser("Esi Quayson", "esi@demo.com", Roles.Employee, "People");
        var kofi = MakeUser("Kofi Boateng", "kofi@demo.com", Roles.Employee, "Sales");
        var manager = MakeUser("Nana Adjei", "manager@demo.com", Roles.Manager, "Operations");
        db.Users.AddRange(ama, kwame, esi, kofi, manager);

        // ---- Sample bookings for "today" (computed at seed time, local -> UTC) ----
        var today = DateTime.Today; // local midnight, Kind=Local
        DateTime At(int hour, int minute = 0) => today.AddHours(hour).AddMinutes(minute).ToUniversalTime();

        db.Bookings.AddRange(
            new Booking
            {
                Workspace = desk2,
                User = kwame,
                StartsAt = At(9),
                EndsAt = At(17),
                Note = "Focus day",
            },
            new Booking
            {
                Workspace = desk7,
                User = ama,
                StartsAt = At(9),
                EndsAt = At(12, 30),
                Note = "Design sync",
            },
            new Booking
            {
                Workspace = desk11,
                User = esi,
                StartsAt = At(13),
                EndsAt = At(16),
            });

        db.SaveChanges();
    }
}
