using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using appErasmus.Models;

namespace appErasmus.Data {
    /// <summary>
    /// Contexto de acesso à base de dados da aplicação appErasmus.
    /// Centraliza o mapeamento das tabelas no SQL Server e herda do IdentityDbContext para gerir a autenticação.
    /// </summary>
    public class ApplicationDbContext : IdentityDbContext<Utilizador> {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) {
        }

        /// <summary> Tabela de perfis específicos de Alunos (extensão de Utilizador). </summary>
        public DbSet<Aluno> Alunos { get; set; }

        /// <summary> Tabela de Cursos do politécnico. </summary>
        public DbSet<Curso> Cursos { get; set; }

        /// <summary> Tabela das Escolas associadas à propria instituição. </summary>
        public DbSet<Escola> Escolas { get; set; }

        /// <summary> Graus academicos de cada curso. </summary>
        public DbSet<GrauCurso> GrausCurso { get; set; } 

        /// <summary> Tabela principal de Candidaturas Erasmus (Chave Primária por GUID). </summary>
        public DbSet<Candidatura> Candidaturas { get; set; }

        /// <summary> Tabela de catálogo de Universidades parceiras. </summary>
        public DbSet<Universidade> Universidades { get; set; }

        /// <summary> Tabela intermédia Muitos-para-Muitos para escolhas de Universidades por candidatura (Chave Composta). </summary>
        public DbSet<CandidaturaUniversidade> CandidaturasUniversidades { get; set; }

        /// <summary> Tabela de competências linguísticas associadas às candidaturas. </summary>
        public DbSet<CandidaturaLingua> CandidaturasLinguas { get; set; }

        /// <summary> Tabela de metadados dos Ficheiros/Documentos submetidos. </summary>
        public DbSet<Documento> Documentos { get; set; }

        /// <summary> Tabela de Categorias e regras de destino dos documentos. </summary>
        public DbSet<CategoriaDocumento> CategoriasDocumentos { get; set; }

        /// <summary>
        /// Configuração avançada do mapeamento relacional através da Fluent API.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder builder) {
            // CRÍTICO: Executa as configurações base das tabelas do Identity (AspNetUsers, AspNetRoles, etc.)
            base.OnModelCreating(builder);

            // --- CANDIDATURA UNIVERSIDADE (Chave Composta) ---
            // Mapeamento explícito da chave composta para reforçar a anotação contida no modelo
            builder.Entity<CandidaturaUniversidade>()
                .HasKey(cu => new { cu.CandidaturaFK, cu.UniversidadeFK });

            // --- CONFIGURAÇÃO DE RELACIONAMENTOS DO MODELO DOCUMENTO ---

            // Relacionamento entre Documento e CategoriaDocumento
            builder.Entity<Documento>()
                .HasOne(d => d.Categoria)
                .WithMany(c => c.Documentos)
                .HasForeignKey(d => d.CategoriaFK) // Atualizado de CategoriaNome para CategoriaFK
                .OnDelete(DeleteBehavior.Restrict);

            // Relacionamento entre Documento e Candidatura
            builder.Entity<Documento>()
                .HasOne(d => d.Candidatura)
                .WithMany(c => c.Documentos)
                .HasForeignKey(d => d.CandidaturaFK) // Agora mapeia o tipo GUID mapeado automaticamente
                .OnDelete(DeleteBehavior.Cascade);

            // Relacionamento de Auditoria: Criador do Documento
            builder.Entity<Documento>()
                .HasOne(d => d.CreatedBy)
                .WithMany()
                .HasForeignKey(d => d.CreatedByFK)
                .OnDelete(DeleteBehavior.Restrict);

            // Relacionamento de Auditoria: Último a Atualizar o Documento
            builder.Entity<Documento>()
                .HasOne(d => d.UpdatedBy)
                .WithMany()
                .HasForeignKey(d => d.UpdatedByFK)
                .OnDelete(DeleteBehavior.Restrict);

            // NOTA DE ARQUITETURA: O bloco de relacionamento antigo "Documento -> Destinatario" 
            // foi removido por completo deste método, dado que a propriedade agora reside 
            // como um campo de texto funcional (RoleDestinatario) na entidade CategoriaDocumento.
        }
    }
}