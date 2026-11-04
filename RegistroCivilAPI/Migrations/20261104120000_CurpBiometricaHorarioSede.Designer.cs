using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RegistroCivilAPI.Models;

#nullable disable

namespace RegistroCivilAPI.Migrations
{
    [DbContext(typeof(RegistroCivilCitasContext))]
    [Migration("20261104120000_CurpBiometricaHorarioSede")]
    partial class CurpBiometricaHorarioSede
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            new SnapshotBridge().Apply(modelBuilder);
        }

        private sealed class SnapshotBridge : RegistroCivilCitasContextModelSnapshot
        {
            public void Apply(ModelBuilder modelBuilder) => BuildModel(modelBuilder);
        }
    }
}
