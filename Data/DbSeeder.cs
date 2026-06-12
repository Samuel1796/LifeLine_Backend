using BloodDonorFinder.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace BloodDonorFinder.Api.Data;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (db.Users.Any()) return;

        var hasher = new PasswordHasher<User>();

        // Demo donors across Accra, Kumasi and Takoradi. All passwords: Password123!
        var donors = new (string Name, string Email, string BloodType, double Lat, double Lng, string City, bool Available, bool Anonymous)[]
        {
            // Accra
            ("Kwame Mensah",   "kwame@demo.com",   "O-",  5.6037, -0.1870, "Accra",    true,  false),
            ("Ama Owusu",      "ama@demo.com",     "A+",  5.6500, -0.1962, "Accra",    true,  true),
            ("Kofi Boateng",   "kofi@demo.com",    "B+",  5.5560, -0.1969, "Accra",    false, false),
            ("Adwoa Sarpong",  "adwoa@demo.com",   "AB-", 5.6310, -0.1715, "Accra",    true,  false),
            // Kumasi
            ("Akosua Asante",  "akosua@demo.com",  "O+",  6.6885, -1.6244, "Kumasi",   true,  false),
            ("Yaw Darko",      "yaw@demo.com",     "AB+", 6.6745, -1.5716, "Kumasi",   true,  true),
            ("Abena Frimpong", "abena@demo.com",   "A-",  6.6666, -1.6163, "Kumasi",   false, true),
            ("Kwabena Osei",   "kwabena@demo.com", "B+",  6.7010, -1.6300, "Kumasi",   true,  false),
            // Takoradi
            ("Kojo Eshun",     "kojo@demo.com",    "B-",  4.8956, -1.7557, "Takoradi", true,  false),
            ("Esi Quayson",    "esi@demo.com",     "O+",  4.9152, -1.7734, "Takoradi", true,  true),
            ("Fiifi Arthur",   "fiifi@demo.com",   "A+",  4.9054, -1.7860, "Takoradi", false, false),
            ("Efua Mensimah",  "efua@demo.com",    "O-",  4.9101, -1.7601, "Takoradi", true,  true),
        };

        var phoneCounter = 1;
        foreach (var (name, email, bloodType, lat, lng, city, available, anonymous) in donors)
        {
            var user = new User
            {
                FullName = name,
                Email = email,
                Role = Roles.Donor,
                Phone = $"+233 20 000 00{phoneCounter++:00}",
            };
            user.PasswordHash = hasher.HashPassword(user, "Password123!");
            user.DonorProfile = new DonorProfile
            {
                BloodType = bloodType,
                Latitude = lat,
                Longitude = lng,
                City = city,
                IsAvailable = available,
                IsAnonymous = anonymous,
            };
            db.Users.Add(user);
        }

        var requester = new User
        {
            FullName = "Efua Hospital Admin",
            Email = "requester@demo.com",
            Role = Roles.Requester,
            Phone = "+233 30 000 0000",
        };
        requester.PasswordHash = hasher.HashPassword(requester, "Password123!");
        db.Users.Add(requester);

        db.SaveChanges();
    }
}
