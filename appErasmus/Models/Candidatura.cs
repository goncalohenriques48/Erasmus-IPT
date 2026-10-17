using appErasmus.Models.Validations;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace appErasmus.Models {
    /// <summary>
    /// Define os possíveis estados de uma candidatura no fluxo do Backoffice.
    /// </summary>
    public enum EnumEstadoCandidatura {
        /// <summary> O aluno desistiu formalmente do processo de mobilidade. </summary>
        [Display(Name = "Desistência")]
        Desistencia = 0,

        /// <summary> A candidatura está a ser preenchida pelo aluno e ainda não foi submetida. </summary>
        [Display(Name = "Em Construção")]
        EmConstrucao = 1,

        /// <summary> A candidatura foi enviada e aguarda validação do Coordenador de Erasmus. </summary>
        [Display(Name = "Submetida (Em Processamento)")]
        Submetida = 2,

        /// <summary> O Coordenador de Erasmus rejeitou o processo. </summary>
        [Display(Name = "Rejeitada (Coord. Erasmus)")]
        RejeitadaCoordenador = 3,

        /// <summary> O processo foi aprovado pelo Coordenador e está em análise final no GRI. </summary>
        [Display(Name = "Em Análise (GRI)")]
        EmAnaliseGRI = 4,

        /// <summary> O GRI ou a instituição de destino não aceitou a candidatura do estudante. </summary>
        [Display(Name = "Rejeitada (Não Aceite)")]
        RejeitadaNaoAceite = 5,

        /// <summary> A candidatura foi aceite por todas as entidades envolvidas. </summary>
        [Display(Name = "Aceite")]
        Aceite = 6,

        /// <summary> A candidatura foi recusada e foi solicitada para correção. </summary>
        [Display(Name = "Devolvida para Correção")]
        DevolvidaParaCorrecao = 7,

        /// <summary> O aluno regressou de mobilidade, submeteu os documentos finais e o processo aguarda validação de fecho pelo GRI. </summary>
        [Display(Name = "Pós-Mobilidade (Em Análise)")]
        EmAnaliseFinalGRI = 8,

        /// <summary> Todo o processo administrativo e de mobilidade foi concluído com sucesso. </summary>
        [Display(Name = "Finalizado")]
        Finalizado = 99
    }

    /// <summary>
    /// Define quem solicitou a correção da candidatura.
    /// Permite devolver a candidatura para a etapa correta depois de o aluno corrigir e voltar a submeter.
    /// </summary>
    public enum EnumOrigemCorrecao
    {
        /// <summary> Não existe correção pendente ou a candidatura não se encontra num fluxo de devolução. </summary>
        [Display(Name = "Inexistente")]
        None = 0,

        /// <summary>
        /// A correção foi solicitada pelo Coordenador Erasmus.
        /// Após a resposta do aluno, a candidatura deve voltar para análise do coordenador.
        /// </summary>
        [Display(Name = "Coordenador Erasmus")]
        Coordenador = 1,

        /// <summary>
        /// A correção foi solicitada pelo GRI.
        /// Após a resposta do aluno, a candidatura deve voltar diretamente para análise do GRI.
        /// </summary>
        [Display(Name = "GRI")]
        GRI = 2
    }

    /// <summary>
    /// Define as opções de período temporal (semestres ou anual) para a mobilidade.
    /// </summary>
    public enum EnumSemestre {
        /// <summary> Mobilidade planeada apenas para o primeiro semestre letivo. </summary>
        [Display(Name = "1º Semestre")]
        PrimeiroSemestre = 1,

        /// <summary> Mobilidade planeada apenas para o segundo semestre letivo. </summary>
        [Display(Name = "2º Semestre")]
        SegundoSemestre = 2,

        /// <summary> Mobilidade para o ano letivo completo (ambos os semestres). </summary>
        [Display(Name = "Ano Inteiro (Anual)")]
        Anual = 3
    }

    /// <summary>
    /// Classe que representa uma Candidatura ao programa Erasmus.
    /// Centraliza as escolhas de mobilidade do estudante e o estado do workflow.
    /// </summary>
    public class Candidatura {
        /// <summary>
        /// Identificador único da candidatura. 
        /// Utiliza um GUID (Global Unique Identifier) para prevenir ataques de IDOR na visualização dos registos.
        /// </summary>
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Chave estrangeira que liga a candidatura ao Aluno proprietário do registo.
        /// Mapeia o identificador do Identity User (GUID).
        /// </summary>
        [ValidateNever]
        [Display(Name = "Identificador do Aluno")]
        [ForeignKey(nameof(Aluno))]
        public string AlunoFK { get; set; } = string.Empty;

        /// <summary>
        /// Propriedade de navegação para obter os dados completos do Aluno.
        /// </summary>
        [ValidateNever]
        public Aluno Aluno { get; set; } = null!;

        /// <summary>
        /// Ano letivo a que o aluno se ESTÁ A CANDIDATAR para realizar a mobilidade.
        /// Exemplo: Se o processo decorre em 2025 para viajar no ano seguinte, o valor guardado será "2026/2027".
        /// </summary>
        [Required(ErrorMessage = "O {0} é de preenchimento obrigatório.")]
        [Display(Name = "Ano Letivo de Candidatura")]
        public string AnoLetivo { get; set; } = string.Empty;

        /// <summary>
        /// Semestre em que o aluno pretende realizar a mobilidade Erasmus (1º, 2º ou Anual).
        /// </summary>
        [Range(1, 3, ErrorMessage = "O semestre da mobilidade é de preenchimento obrigatório.")] 
        [Display(Name = "Semestre da Mobilidade")]
        public EnumSemestre Semestre { get; set; }

        /// <summary>
        /// Data e hora em que a candidatura foi submetida oficialmente.
        /// Inicializada por omissão com a data atual do sistema.
        /// </summary>
        [Display(Name = "Data de Submissão")]
        public DateTime DataSubmissao { get; set; } = DateTime.Now;

        /// <summary>
        /// Data em que o processo foi validado ou despachado pelo backoffice (nulo se ainda estiver pendente).
        /// </summary>
        [Display(Name = "Data de Validação")]
        public DateTime? DataValidacao { get; set; }

        /// <summary>
        /// Sinaliza se o estudante usufrui do estatuto de bolseiro por parte dos SAS.
        /// </summary>
        [Display(Name = "Bolseiro")]
        public bool Bolseiro { get; set; }

        /// <summary>
        /// IBAN da conta bancária para processamento de bolsas ou apoios financeiros.
        /// Suporta o formato padrão internacional (SEPA/ISO 13616) para abranger alunos nacionais e internacionais.
        /// </summary>
        [Required(ErrorMessage = "O IBAN é de preenchimento obrigatório.")]
        [Display(Name = "IBAN")]
        [StringLength(50, ErrorMessage = "O {0} não pode exceder os {1} caracteres.")]
        [ValidarIBAN] 
        public string IBAN { get; set; } = string.Empty;

        /// <summary>
        /// Estado atual em que a candidatura se encontra na máquina de estados do Backoffice.
        /// </summary>
        [ValidateNever]
        [Display(Name = "Estado da Candidatura")]
        public EnumEstadoCandidatura EstadoCandidatura { get; set; } = EnumEstadoCandidatura.EmConstrucao;

        /// <summary>
        /// Coleção de relações intermédias para mapear as Universidades escolhidas nesta candidatura (Muitos-para-Muitos).
        /// </summary>
        [ValidateNever]
        [Display(Name = "Universidades de Destino")]
        public ICollection<CandidaturaUniversidade> CandidaturasUniversidades { get; set; } = [];

        /// <summary>
        /// Coleção de documentos e ficheiros anexados e submetidos para esta candidatura.
        /// </summary>
        [ValidateNever]
        [Display(Name = "Documentos Anexados")]
        public ICollection<Documento> Documentos { get; set; } = [];

        /// <summary>
        /// Coleção de competências linguísticas declaradas ou comprovadas nesta candidatura.
        /// </summary>
        [Display(Name = "Competências Linguísticas")]
        public ICollection<CandidaturaLingua> Linguas { get; set; } = [];

        /// <summary>
        /// Identifica que entidade solicitou a correção da candidatura.
        /// Este campo é usado para reenviar a candidatura para a etapa correta
        /// quando o aluno volta a submeter depois de uma devolução para correção.
        /// </summary>
        [Display(Name = "Origem da Correção")]
        public EnumOrigemCorrecao OrigemCorrecao { get; set; } = EnumOrigemCorrecao.None;

        /// <summary>
        /// Chave estrangeira que associa a candidatura ao curso no qual o aluno se encontra inscrito.
        /// </summary>
        [Display(Name = "Curso da Candidatura")]
        public int CursoFK { get; set; }

        /// <summary>
        /// Propriedade de navegação para obter a informação completa do curso.
        /// </summary>
        [ValidateNever]
        [ForeignKey(nameof(CursoFK))]
        public Curso Curso { get; set; } = null!;

        /// <summary>
        /// Armazena o feedback textual escrito pelo Coordenador ou pelo GRI 
        /// quando a candidatura é rejeitada ou devolvida para correção.
        /// </summary>
        [Display(Name = "Motivo de Devolução/Rejeição")]
        [StringLength(1000, ErrorMessage = "O motivo não pode exceder os {1} caracteres.")]
        public string MotivoRejeicao { get; set; } = string.Empty;

        /// <summary>
        /// Flag de controlo que indica se a candidatura foi reaberta para edição por parte do aluno.
        /// Usado para bloquear edições não autorizadas através da alteração do URL.
        /// </summary>
        [Display(Name = "Permite Correção pelo Aluno")]
        public bool PermiteCorrecao { get; set; } = false;

    }
}