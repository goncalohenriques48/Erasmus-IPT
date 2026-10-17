using System.ComponentModel.DataAnnotations;

namespace appErasmus.Models
{
    /// <summary>
    /// Representa o grau académico ou a tipologia de um curso lecionado na instituição.
    /// Usado para categorizar a oferta formativa (ex: TeSP, Licenciatura, Mestrado).
    /// </summary>
    public class GrauCurso
    {
        /// <summary>
        /// Identificador único do grau académico na base de dados (Chave Primária).
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Sigla ou código curto que identifica o grau (ex: "L" para Licenciatura, "M" para Mestrado, "CTeSP").
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Código do Grau")]
        public string CodigoGrau { get; set; } = string.Empty;

        /// <summary>
        /// Designação completa e oficial do grau académico (ex: "Curso Técnico Superior Profissional", "Licenciatura").
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Grau")]
        public string Nome { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação: Relação Um-para-Muitos com os Cursos.
        /// Agrupa todos os cursos do Politécnico que partilham este mesmo grau académico.
        /// </summary>
        public virtual ICollection<Curso> Cursos { get; set; } = [];
    }
}