using RefineryContractAPI.Services;
using RefineryContractAPI.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.OpenApi.Models;
using RefineryContractAPI.Data;
using System.Text;


AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ==================== REQUEST SIZE LIMIT ====================
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
});

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
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var jti = context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (!string.IsNullOrEmpty(jti))
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var isBlacklisted = await db.TokenBlacklists.AnyAsync(t => t.Jti == jti);
                    if (isBlacklisted)
                        context.Fail("Token has been revoked");
                }
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<R2StorageService>();
builder.Services.AddSingleton<FileTokenService>();
builder.Services.AddSingleton<AiExtractionService>();
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
    //
    // PENTING: setiap ExecuteSqlRaw dikirim sebagai satu batch statement ke
    // Postgres, yang menjalankannya dalam satu transaksi implisit — kalau ada
    // SATU statement yang gagal (mis. ON CONFLICT butuh unique constraint yang
    // belum tentu ada di tabel sla_setting yang dibuat manual), SEMUA statement
    // lain di batch yang sama ikut di-rollback, termasuk ALTER TABLE ADD COLUMN
    // yang seharusnya tidak berhubungan. Makanya blok ini dipecah per kelompok
    // supaya kegagalan di satu bagian (mis. seed sla_setting) tidak menggagalkan
    // penambahan kolom sla_tagihan yang dibutuhkan endpoint create/delete tagihan.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS no_irkap TEXT;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS s_curve_data TEXT;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS tanggal_mpl INTEGER;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS tanggal_mpa INTEGER;
            ALTER TABLE kontrak ADD COLUMN IF NOT EXISTS masa_pemeliharaan_hari INTEGER;
            CREATE TABLE IF NOT EXISTS token_blacklist (
                id SERIAL PRIMARY KEY,
                jti TEXT NOT NULL UNIQUE,
                expires_at TIMESTAMP NOT NULL,
                revoked_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_token_blacklist_jti ON token_blacklist(jti);
            DELETE FROM token_blacklist WHERE expires_at < NOW();
            CREATE TABLE IF NOT EXISTS log_akses (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL DEFAULT '',
                nama_user TEXT NOT NULL DEFAULT '',
                role TEXT NOT NULL DEFAULT '',
                menu TEXT NOT NULL DEFAULT '',
                activity TEXT NOT NULL DEFAULT '',
                detail TEXT,
                ip_address TEXT,
                created_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_log_akses_created_at ON log_akses(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_log_akses_user_id ON log_akses(user_id);
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Column migration warning (kontrak/token_blacklist/log_akses): {ex.Message}");
    }

    try
    {
        db.Database.ExecuteSqlRaw(@"
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_masuk_progress_eksekusi TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_selesai_progress_eksekusi TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_masuk_ba_joint_inspection TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_selesai_ba_joint_inspection TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_masuk_ba_commissioning TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_selesai_ba_commissioning TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_masuk_ba_penerimaan_material TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_selesai_ba_penerimaan_material TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_masuk_perhitungan TIMESTAMP;
            ALTER TABLE sla_tagihan ADD COLUMN IF NOT EXISTS tgl_selesai_perhitungan TIMESTAMP;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Column migration warning (sla_tagihan): {ex.Message}");
    }

    try
    {
        db.Database.ExecuteSqlRaw(@"
            INSERT INTO sla_setting (kode_tahap, batas_hari, warning_persen)
            VALUES
              ('BA_JOINT_INSPECTION',    7, 80),
              ('BA_COMMISSIONING',       7, 80),
              ('BA_PENERIMAAN_MATERIAL', 7, 80),
              ('PERHITUNGAN',            7, 80),
              ('BASTP',                  7, 80)
            ON CONFLICT (kode_tahap) DO NOTHING;
            DELETE FROM sla_setting WHERE kode_tahap IN ('PUNCHLIST', 'BAST');
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Seed warning (sla_setting): {ex.Message}");
    }

    try
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS rab_item (
                id_rab_item TEXT PRIMARY KEY,
                id_kontrak TEXT NOT NULL,
                kode_item TEXT NOT NULL,
                kategori TEXT,
                uraian_pekerjaan TEXT NOT NULL,
                satuan TEXT NOT NULL,
                harga_satuan_upah NUMERIC,
                harga_satuan_material NUMERIC,
                harga_satuan_alat NUMERIC,
                created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_rab_item_id_kontrak ON rab_item(id_kontrak);

            CREATE TABLE IF NOT EXISTS material_requirement_draft (
                id_draft TEXT PRIMARY KEY,
                id_kontrak TEXT NOT NULL,
                nomor_mrf TEXT,
                tag_unit TEXT NOT NULL,
                lokasi_area TEXT,
                tanggal_rekomendasi TIMESTAMP,
                problem TEXT,
                rekomendasi_solusi TEXT,
                status TEXT NOT NULL DEFAULT 'Draft',
                rekomendasi_documents TEXT,
                gambar_kerja_documents TEXT,
                catatan TEXT,
                created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_mr_draft_id_kontrak ON material_requirement_draft(id_kontrak);

            CREATE TABLE IF NOT EXISTS material_requirement_line (
                id_line TEXT PRIMARY KEY,
                id_draft TEXT NOT NULL,
                jenis TEXT NOT NULL DEFAULT 'Pekerjaan',
                id_rab_item TEXT,
                kode_item_snapshot TEXT,
                uraian_pekerjaan TEXT NOT NULL,
                satuan TEXT NOT NULL,
                volume_kalkulasi NUMERIC NOT NULL DEFAULT 0,
                catatan_kalkulasi TEXT,
                volume_klaim NUMERIC,
                urutan INTEGER NOT NULL DEFAULT 0,
                created_at TIMESTAMP NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_mr_line_id_draft ON material_requirement_line(id_draft);
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Table creation warning (rab_item/material_requirement_*): {ex.Message}");
    }

    try
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS kontrak_favorit (
                id_favorit TEXT PRIMARY KEY,
                id_user TEXT NOT NULL,
                id_kontrak TEXT NOT NULL,
                created_at TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_kontrak_favorit_user_kontrak ON kontrak_favorit(id_user, id_kontrak);
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Table creation warning (kontrak_favorit): {ex.Message}");
    }

    // Backfill: kontrak yang punya amandemen di tabel amandemen_kontrak tapi kolom tanggal
    // amandemen-nya di tabel kontrak masih kosong/usang (sebelum AmandemenController
    // menyinkronkannya). Amandemen terakhir (nomor_urut tertinggi yang mengisi tanggal) menang;
    // idempotent karena hanya mengubah baris yang nilainya berbeda.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            UPDATE kontrak k SET tanggal_selesai_baru = a.tanggal_selesai_baru
            FROM (
                SELECT DISTINCT ON (id_kontrak) id_kontrak, tanggal_selesai_baru
                FROM amandemen_kontrak
                WHERE tanggal_selesai_baru IS NOT NULL
                ORDER BY id_kontrak, nomor_urut DESC
            ) a
            WHERE k.id_kontrak = a.id_kontrak
              AND k.tanggal_selesai_baru IS DISTINCT FROM a.tanggal_selesai_baru;

            UPDATE kontrak k SET tanggal_mulai_baru = a.tanggal_mulai_baru
            FROM (
                SELECT DISTINCT ON (id_kontrak) id_kontrak, tanggal_mulai_baru
                FROM amandemen_kontrak
                WHERE tanggal_mulai_baru IS NOT NULL
                ORDER BY id_kontrak, nomor_urut DESC
            ) a
            WHERE k.id_kontrak = a.id_kontrak
              AND k.tanggal_mulai_baru IS DISTINCT FROM a.tanggal_mulai_baru;

            UPDATE kontrak SET has_amendment = TRUE
            WHERE id_kontrak IN (SELECT id_kontrak FROM amandemen_kontrak)
              AND has_amendment IS DISTINCT FROM TRUE;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Startup] Backfill warning (tanggal amandemen kontrak): {ex.Message}");
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
app.UseMiddleware<AuditMiddleware>();
app.MapControllers();

app.Run();