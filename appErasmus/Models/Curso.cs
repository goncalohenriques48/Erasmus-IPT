using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace appErasmus.Models {
    /// <summary>
    /// Enum que define os diferentes graus académicos.
    /// </summary>
   /* public enum EnumTipoCurso {
        TeSP = 1,
        Licenciatura = 2,
        Mestrado = 3,
        Doutoramento = 4
    }

    /// <summary>
    /// Enum que define as Escolas que compõem o Instituto Politécnico.
    /// </summary>
    public enum EnumEscola {
        ESTT = 1,
        ESGT = 2,
        ESTA = 3
    }*/

    /// <summary>
    /// Classe que representa um curso lecionado na instituição.
    /// </summary>
    public class Curso {
        /// <summary>
        /// Identificador único do curso na base de dados (Chave Primária).
        /// </summary>
        [Key]
        public int Id { get; set; }


        // --- Dados do Curso ---

        /// <summary>
        /// Chave estrangeira que vincula o curso à respetiva Escola do Instituto Politécnico (ex: ESTT, ESGT, ESTA).
        /// </summary>
        [Required(ErrorMessage = "A Escola é de preenchimento obrigatório.")]
        [Display(Name = "Escola")]
        public int EscolaId { get; set; }

        /// <summary>
        /// Propriedade de navegação para a Escola a que o curso pertence.
        /// </summary>
        [ForeignKey(nameof(EscolaId))]
        public Escola Escola { get; set; } = null!;

        /// <summary>
        /// Chave estrangeira que define a tipologia/grau académico do curso (TeSP, Licenciatura, Mestrado, etc.).
        /// </summary>
        [Required(ErrorMessage = "A Tipologia do Curso é de preenchimento obrigatório.")]
        [Display(Name = "Tipologia do Curso")]
        public int GrauCursoId { get; set; }

        /// <summary>
        /// Propriedade de navegação para obter os detalhes do Grau Académico do curso.
        /// </summary>
        [ForeignKey(nameof(GrauCursoId))]
        public GrauCurso GrauCurso { get; set; } = null!;

        /// <summary>
        /// Código oficial do curso (habitualmente o código atribuído pela DGES).
        /// Requerido para a integração em documentação oficial e plataformas externas.
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [StringLength(6, MinimumLength = 4, ErrorMessage = "O {0} deve ter entre {2} e {1} caracteres.")]
        [RegularExpression(@"^\d{4,6}$", ErrorMessage = "O Código do Curso deve conter apenas números (entre 4 e 6 dígitos).")]
        [Display(Name = "Código do Curso")]
        public string CodigoCurso { get; set; } = string.Empty;

        /// <summary>
        /// Designação completa e oficial do curso na língua portuguesa.
        /// </summary>
        [Required(ErrorMessage = "O nome do curso em Português é de preenchimento obrigatório.")]
        [StringLength(100, ErrorMessage = "O {0} não pode exceder os {1} caracteres.")]
        [Display(Name = "Nome do Curso (PT)")]
        public string NomePTCurso { get; set; } = string.Empty;

        /// <summary>
        /// Designação do curso traduzida para a língua inglesa.
        /// Fundamental para o preenchimento automático de documentação de mobilidade internacional (ex: Learning Agreement).
        /// </summary>
        [Required(ErrorMessage = "O nome do curso em Inglês é de preenchimento obrigatório.")]
        [StringLength(100, ErrorMessage = "O {0} não pode exceder os {1} caracteres.")]
        [Display(Name = "Nome do Curso (EN)")]
        public string NomeENCurso { get; set; } = string.Empty;

        /// <summary>
        /// Define se o curso se encontra atualmente em funcionamento/ativo no sistema.
        /// Permite manter o histórico de candidaturas em cursos que entretanto foram descontinuados.
        /// </summary>
        [Display(Name = "Curso Ativo")]
        public bool IsActive { get; set; } = true;

        // --- Relações e Responsáveis ---

        /// <summary>
        /// Nome completo do Diretor de Curso atual.
        /// </summary>
        [Required(ErrorMessage = "O nome do Diretor de Curso é de preenchimento obrigatório.")]
        [Display(Name = "Nome do Diretor de Curso")]
        public string NomeDiretor { get; set; } = string.Empty;

        /// <summary>
        /// Endereço de email institucional do Diretor de Curso, usado para contactos de âmbito letivo.
        /// </summary>
        [Required(ErrorMessage = "O email do Diretor de Curso é de preenchimento obrigatório.")]
        [EmailAddress(ErrorMessage = "O formato do email inserido é inválido.")]
        [Display(Name = "Email do Diretor de Curso")]
        public string EmailDiretor { get; set; } = string.Empty;

        /// <summary>
        /// Chave estrangeira que referencia o Utilizador com a Role "CoordenadorErasmus" responsável por este curso.
        /// Pode ser nulo caso o curso temporariamente não tenha coordenador atribuído.
        /// </summary>
        [Display(Name = "Coordenador Erasmus")]
        public string? CoordenadorFK { get; set; }

        /// <summary>
        /// Propriedade de navegação para obter os dados do Utilizador que atua como Coordenador Erasmus do curso.
        /// </summary>
        [ForeignKey(nameof(CoordenadorFK))] 
        public Utilizador Coordenador { get; set; } = null!;

        /// <summary>
        /// Propriedade de navegação: Relação Muitos-para-Muitos com os Alunos inscritos.
        /// </summary>
        public ICollection<Aluno> Alunos { get; set; } = [];

        /// <summary>
        /// Histórico de todas as candidaturas de mobilidade que foram submetidas ao abrigo deste curso.
        /// </summary>
        [Display(Name = "Candidaturas")]
        public ICollection<Candidatura> Candidaturas { get; set; } = [];

    }
}