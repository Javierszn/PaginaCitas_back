using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace RegistroCivilAPI.Models;

public partial class RegistroCivilCitasContext : DbContext
{
    public RegistroCivilCitasContext() { }

    public RegistroCivilCitasContext(DbContextOptions<RegistroCivilCitasContext> options)
        : base(options) { }

    public virtual DbSet<BitacoraAuditorium> BitacoraAuditoria { get; set; }
    public virtual DbSet<CategoriasTramite> CategoriasTramites { get; set; }
    public virtual DbSet<Cita> Citas { get; set; }
    public virtual DbSet<Ciudadano> Ciudadanos { get; set; }
    public virtual DbSet<DiasInhabile> DiasInhabiles { get; set; }
    public virtual DbSet<HorariosSede> HorariosSedes { get; set; }
    public virtual DbSet<Role> Roles { get; set; }
    public virtual DbSet<Sede> Sedes { get; set; }
    public virtual DbSet<Tramite> Tramites { get; set; }
    public virtual DbSet<UsuariosInterno> UsuariosInternos { get; set; }
    public virtual DbSet<AvisoGlobal> AvisosGlobales { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlServer("Server=localhost\\SQLEXPRESS;Database=RegistroCivil_Citas;Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BitacoraAuditorium>(entity =>
        {
            entity.HasKey(e => e.IdBitacora).HasName("PK__Bitacora__7E4268B0D2EB1BA4");
            entity.ToTable("Bitacora_Auditoria");
            entity.Property(e => e.IdBitacora).HasColumnName("id_bitacora");
            entity.Property(e => e.AccionRealizada).HasMaxLength(20).IsUnicode(false).HasColumnName("accion_realizada");
            entity.Property(e => e.FechaCambio).HasDefaultValueSql("(getdate())").HasColumnType("datetime").HasColumnName("fecha_cambio");
            entity.Property(e => e.IdUsuarioInterno).HasColumnName("id_usuario_interno");
            entity.Property(e => e.RegistroId).HasColumnName("registro_id");
            entity.Property(e => e.TablaAfectada).HasMaxLength(50).IsUnicode(false).HasColumnName("tabla_afectada");
            entity.Property(e => e.ValorAnterior).HasColumnType("text").HasColumnName("valor_anterior");
            entity.Property(e => e.ValorNuevo).HasColumnType("text").HasColumnName("valor_nuevo");
            entity.HasOne(d => d.IdUsuarioInternoNavigation).WithMany(p => p.BitacoraAuditoria)
                .HasForeignKey(d => d.IdUsuarioInterno)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Bitacora___id_us__59063A47");
        });

        modelBuilder.Entity<CategoriasTramite>(entity =>
        {
            entity.HasKey(e => e.IdCategoria).HasName("PK__Categori__CD54BC5ADDE5FCC4");
            entity.ToTable("Categorias_Tramites");
            entity.Property(e => e.IdCategoria).HasColumnName("id_categoria");
            entity.Property(e => e.NombreCategoria).HasMaxLength(100).IsUnicode(false).HasColumnName("nombre_categoria");
            entity.Property(e => e.Descripcion).HasColumnType("varchar(255)").HasColumnName("descripcion");
            entity.Property(e => e.Activa).HasDefaultValue(true).HasColumnName("activa");
        });

        modelBuilder.Entity<Cita>(entity =>
        {
            entity.HasKey(e => e.IdCita).HasName("PK__Citas__6AEC3C092F0BF515");
            entity.Property(e => e.IdCita).HasMaxLength(20).IsUnicode(false).HasColumnName("id_cita");
            entity.Property(e => e.Estatus).HasMaxLength(20).IsUnicode(false).HasColumnName("estatus");
            entity.Property(e => e.FechaHoraFin).HasColumnType("datetime").HasColumnName("fecha_hora_fin");
            entity.Property(e => e.FechaHoraInicio).HasColumnType("datetime").HasColumnName("fecha_hora_inicio");
            entity.Property(e => e.IdCiudadano).HasColumnName("id_ciudadano");
            entity.Property(e => e.IdSede).HasColumnName("id_sede");
            entity.Property(e => e.IdTramite).HasColumnName("id_tramite");
            entity.HasOne(d => d.IdCiudadanoNavigation).WithMany(p => p.Citas)
                .HasForeignKey(d => d.IdCiudadano)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Citas__id_ciudad__4CA06362");
            entity.HasOne(d => d.IdSedeNavigation).WithMany(p => p.Citas)
                .HasForeignKey(d => d.IdSede)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Citas__id_sede__4E88ABD4");
            entity.HasOne(d => d.IdTramiteNavigation).WithMany(p => p.Citas)
                .HasForeignKey(d => d.IdTramite)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Citas__id_tramit__4D94879B");
        });

        modelBuilder.Entity<Ciudadano>(entity =>
        {
            entity.HasKey(e => e.IdCiudadano).HasName("PK__Ciudadan__676FF54B1A98BECA");
            entity.HasIndex(e => e.Curp, "UQ__Ciudadan__2CDDD194822D2264").IsUnique();
            entity.Property(e => e.IdCiudadano).HasColumnName("id_ciudadano");
            entity.Property(e => e.Correo).HasMaxLength(100).IsUnicode(false).HasColumnName("correo");
            entity.Property(e => e.Curp).HasMaxLength(18).IsUnicode(false).HasColumnName("curp");
            entity.Property(e => e.Nombre).HasMaxLength(50).IsUnicode(false).HasColumnName("nombre");
            entity.Property(e => e.OrigenRegistro).HasMaxLength(150).IsUnicode(false).HasColumnName("origen_registro");
            entity.Property(e => e.PrimerApellido).HasMaxLength(50).IsUnicode(false).HasColumnName("primer_apellido");
            entity.Property(e => e.SegundoApellido).HasMaxLength(50).IsUnicode(false).HasColumnName("segundo_apellido");
            entity.Property(e => e.Telefono).HasMaxLength(15).IsUnicode(false).HasColumnName("telefono");
        });

        modelBuilder.Entity<DiasInhabile>(entity =>
        {
            entity.HasKey(e => e.IdDiaInhabil).HasName("PK__Dias_Inh__2488E88C2B3103CD");
            entity.ToTable("Dias_Inhabiles");
            entity.Property(e => e.IdDiaInhabil).HasColumnName("id_dia_inhabil");
            entity.Property(e => e.FechaBloqueada).HasColumnName("fecha_bloqueada");
            entity.Property(e => e.IdSede).HasColumnName("id_sede");
            entity.Property(e => e.Motivo).HasMaxLength(100).IsUnicode(false).HasColumnName("motivo");
            entity.HasOne(d => d.IdSedeNavigation).WithMany(p => p.DiasInhabiles)
                .HasForeignKey(d => d.IdSede)
                .HasConstraintName("FK__Dias_Inha__id_se__5441852A");
        });

        modelBuilder.Entity<HorariosSede>(entity =>
        {
            entity.HasKey(e => e.IdHorario).HasName("PK__Horarios__C5836D69FA1A6428");
            entity.ToTable("Horarios_Sede");
            entity.Property(e => e.IdHorario).HasColumnName("id_horario");
            entity.Property(e => e.DiaSemana).HasColumnName("dia_semana");
            entity.Property(e => e.HoraApertura).HasColumnName("hora_apertura");
            entity.Property(e => e.HoraCierre).HasColumnName("hora_cierre");
            entity.Property(e => e.IdSede).HasColumnName("id_sede");
            entity.HasOne(d => d.IdSedeNavigation).WithMany(p => p.HorariosSedes)
                .HasForeignKey(d => d.IdSede)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Horarios___id_se__5165187F");
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(e => e.IdRol).HasName("PK__Roles__6ABCB5E0D56A4E16");
            entity.Property(e => e.IdRol).HasColumnName("id_rol");
            entity.Property(e => e.NombreRol).HasMaxLength(50).IsUnicode(false).HasColumnName("nombre_rol");
        });

        modelBuilder.Entity<Sede>(entity =>
        {
            entity.HasKey(e => e.IdSede).HasName("PK__Sedes__D693504B010B634E");
            entity.Property(e => e.IdSede).HasColumnName("id_sede");
            entity.Property(e => e.Activa).HasDefaultValue(true).HasColumnName("activa");
            entity.Property(e => e.Direccion).HasMaxLength(255).IsUnicode(false).HasColumnName("direccion");
            entity.Property(e => e.Municipio).HasMaxLength(50).IsUnicode(false).HasColumnName("municipio");
            entity.Property(e => e.Nombre).HasMaxLength(100).IsUnicode(false).HasColumnName("nombre");
        });

        modelBuilder.Entity<Tramite>(entity =>
        {
            entity.HasKey(e => e.IdTramite).HasName("PK__Tramites__DC2729AE6A30BD91");
            entity.Property(e => e.IdTramite).HasColumnName("id_tramite");
            entity.Property(e => e.Activo).HasDefaultValue(true).HasColumnName("activo");
            entity.Property(e => e.Costo).HasColumnType("decimal(10, 2)").HasColumnName("costo");
            entity.Property(e => e.Descripcion).HasColumnType("text").HasColumnName("descripcion");
            entity.Property(e => e.DuracionMinutos).HasColumnName("duracion_minutos");
            entity.Property(e => e.IdCategoria).HasColumnName("id_categoria");
            entity.Property(e => e.LimiteDiarioSede).HasColumnName("limite_diario_sede");
            entity.Property(e => e.NombreTramite).HasMaxLength(100).IsUnicode(false).HasColumnName("nombre_tramite");
            entity.Property(e => e.Requisitos).HasColumnType("varchar(500)").HasColumnName("requisitos");
            entity.HasOne(d => d.IdCategoriaNavigation).WithMany(p => p.Tramites)
                .HasForeignKey(d => d.IdCategoria)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Tramites__id_cat__44FF419A");
        });

        modelBuilder.Entity<UsuariosInterno>(entity =>
        {
            entity.HasKey(e => e.IdUsuario).HasName("PK__Usuarios__4E3E04ADB8D06CD7");
            entity.ToTable("Usuarios_Internos");
            entity.HasIndex(e => e.Username, "UQ__Usuarios__F3DBC572531AC299").IsUnique();
            entity.Property(e => e.IdUsuario).HasColumnName("id_usuario");
            entity.Property(e => e.Activo).HasDefaultValue(true).HasColumnName("activo");
            entity.Property(e => e.IdRol).HasColumnName("id_rol");
            entity.Property(e => e.IdSede).HasColumnName("id_sede");
            entity.Property(e => e.NombreCompleto).HasMaxLength(100).IsUnicode(false).HasColumnName("nombre_completo");
            entity.Property(e => e.PasswordHash).HasMaxLength(255).IsUnicode(false).HasColumnName("password_hash");
            entity.Property(e => e.Username).HasMaxLength(50).IsUnicode(false).HasColumnName("username");
            entity.HasOne(d => d.IdRolNavigation).WithMany(p => p.UsuariosInternos)
                .HasForeignKey(d => d.IdRol)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Usuarios___id_ro__3E52440B");
            entity.HasOne(d => d.IdSedeNavigation).WithMany(p => p.UsuariosInternos)
                .HasForeignKey(d => d.IdSede)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Usuarios___id_se__3F466844");
        });

        // MAPEADO DE LA NUEVA TABLA AVISOS GLOBALES CON IMAGEN
        modelBuilder.Entity<AvisoGlobal>(entity =>
        {
            entity.HasKey(e => e.IdAviso).HasName("PK_AvisosGlobales");
            entity.ToTable("Avisos_Globales");
            entity.Property(e => e.IdAviso).HasColumnName("id_aviso");
            entity.Property(e => e.Titulo).HasMaxLength(100).IsUnicode(false).HasColumnName("titulo");
            entity.Property(e => e.Mensaje).HasColumnType("text").HasColumnName("mensaje");
            entity.Property(e => e.Activo).HasDefaultValue(true).HasColumnName("activo");
            entity.Property(e => e.ImagenUrl).HasMaxLength(500).IsUnicode(false).HasColumnName("imagen_url");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}