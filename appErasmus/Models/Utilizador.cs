using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace appErasmus.Models
{
    /// <summary>
    /// Classe base que representa um utilizador da aplicação.
    /// Herda de "IdentityUser" (já tem Id, Email, UserName, PasswordHash, etc.).
    /// As permissões (GRI, Coordenador, Aluno, SAS, etc.) são geridas pela tabela AspNetUserRoles do Identity.
    /// </summary>
    public class Utilizador : IdentityUser
    {
        /// <summary>
        /// Nome completo do utilizador (obtido no primeiro login ou preenchido depois).
        /// </summary>
        [Required]
        [Display(Name = "Nome Completo")]
        public string NomeCompleto { get; set; } = string.Empty;

        /// <summary>
        /// Data em que o utilizador fez o primeiro login no sistema.
        /// </summary>
        [Display(Name = "Data de Registo")]
        public DateTime DataRegisto { get; set; } = DateTime.Now;

        /// <summary>
        /// Define se a conta do utilizador está ativa. 
        /// Permite o bloqueio preventivo do acesso (soft delete) sem remover os dados históricos da base de dados.
        /// </summary>
        [Display(Name = "Conta Ativa")]
        public bool IsAtivo { get; set; } = true;

        /// <summary>
        /// Propriedade de navegação: Coleção de cursos onde o utilizador atua como Coordenador Erasmus.
        /// Usa "virtual" para permitir o Lazy Loading do Entity Framework.
        /// </summary>
        [Display(Name = "Cursos Coordenados")]
        public ICollection<Curso> CursosCoordenados { get; set; } = [];
    }
}