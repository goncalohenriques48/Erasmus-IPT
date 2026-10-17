using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace appErasmus.Models {
    /// <summary>
    /// Níveis do Quadro Europeu Comum de Referência para as Línguas (QECR).
    /// </summary>
    public enum EnumNivelLingua {
        [Display(Name = "A1 (Iniciante)")]
        A1 = 1,

        [Display(Name = "A2 (Básico)")]
        A2 = 2,

        [Display(Name = "B1 (Intermédio)")]
        B1 = 3,

        [Display(Name = "B2 (Intermédio Superior)")]
        B2 = 4,

        [Display(Name = "C1 (Avançado)")]
        C1 = 5,

        [Display(Name = "C2 (Domínio Proficiente)")]
        C2 = 6
    }

    /// <summary>
    /// Registo das competências linguísticas declaradas pelo aluno no âmbito da sua candidatura.
    /// </summary>
    public class CandidaturaLingua {
        /// <summary>
        /// Identificador único do registo linguístico na base de dados (Chave Primária).
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Idioma declarado pelo aluno (ex: "Inglês", "Espanhol").
        /// </summary>
        [Required(ErrorMessage = "A {0} é de preenchimento obrigatório.")]
        [Display(Name = "Língua")]
        public string Lingua { get; set; } = string.Empty;

        /// <summary>
        /// Grau de proficiência do aluno no idioma correspondente.
        /// </summary>
        [Required(ErrorMessage = "O nível de proficiência é de preenchimento obrigatório.")]
        [Display(Name = "Nível de Proficiência")]
        public EnumNivelLingua Nivel { get; set; }

        // --- CHAVE ESTRANGEIRA ---

        /// <summary>
        /// Chave estrangeira que liga esta competência à candidatura correspondente (Protegido por GUID).
        /// </summary>
        [Display(Name = "Candidatura")]
        public Guid CandidaturaFK { get; set; }

        /// <summary>
        /// Propriedade de navegação para a candidatura. 
        /// O atributo [ValidateNever] evita que o motor do ASP.NET Core MVC bloqueie a submissão do formulário 
        /// caso o objeto completo da Candidatura não seja passado do HTML para o Controlador.
        /// </summary>
        [ValidateNever] 
        [ForeignKey(nameof(CandidaturaFK))]
        public virtual Candidatura Candidatura { get; set; } = null!;
    }
}