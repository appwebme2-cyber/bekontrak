using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RefineryContractAPI.Data;
using System.Text;


AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ==================== DATABASE (PostgreSQL) ====================
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// ==================== JWT AUTH ====================
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings["Issuer"],
            ValidAudience = jwtSettings["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddDirectoryBrowser();

// ==================== CORS ====================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        var defaultOrigins = new[]
        {
            "http://localhost:5173",
            "http://localhost:3000",
            "http://localhost:8080",
            "https://fekontrak-production.up.railway.app",
            "https://maestrokilang.com",
            "https://www.maestrokilang.com"
        };

        var extraOrigins = (builder.Configuration["AllowedOrigins"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var allOrigins = defaultOrigins.Concat(extraOrigins).ToArray();

        policy.WithOrigins(allOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// ==================== SWAGGER ====================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Refinery Contract API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Contoh: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ==================== AUTO MIGRATE ====================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Tambah kolom yang ditambahkan via migration tapi belum ada di DB
    // (EnsureCreated tidak menjalankan migration, hanya buat schema awal)
    try
    {
        db.Database.ExecuteSqlRaw(@"
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS no_irkap TEXT;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS s_curve_data TEXT;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS tanggal_mpl INTEGER;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS tanggal_mpa INTEGER;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS masa_pemeliharaan_hari INTEGER;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Column migration warning: {ex.Message}");
    }
}

// ==================== MIDDLEWARE ====================
var uploadsPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "uploads");
if (!Directory.Exists(uploadsPath))
    Directory.CreateDirectory(uploadsPath);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Swagger juga aktif di production untuk Railway testing
if (app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseCors("AllowReact");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();