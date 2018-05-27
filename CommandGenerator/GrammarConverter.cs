using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using RoboCup.AtHome.CommandGenerator.ReplaceableTypes;
using Object = RoboCup.AtHome.CommandGenerator.ReplaceableTypes.Object;

namespace RoboCup.AtHome.CommandGenerator
{
	/// <summary>
	/// Converts between grammar formats
	/// </summary>
	public class GrammarConverter
	{


		private List<ProductionRule> rules_queue;
		private HashSet<string> expandedRules;
		private HashSet<string> expandedWildcards;

		HashSet<string> forbiddenCharacters;
		HashSet<string> quotationCharacters;
		bool include_comments = true;

		private Grammar grammar;
		protected TextWriter textWriter;
		protected XmlWriter writer;
		private List<Gesture> gestures;
		private LocationManager locations;
		private List<PersonName> names;
		private ObjectManager objects;
		private List<PredefinedQuestion> questions;
		//private static

		/// <summary>
		/// Initializes a new instance of GrammarConverter
		/// </summary>
		private GrammarConverter()
		{
			this.objects = ObjectManager.Instance;
			this.locations = LocationManager.Instance;
		}

		/// <summary>
		/// Initializes a new instance of GrammarConverter
		/// </summary>
		/// <param name="grammar">The grammar to convert</param>
		/// <param name="gestures">List of gesture names</param>
		/// <param name="names">List of people name</param>
		/// <param name="questions">List of known questions</param>
		public GrammarConverter(Grammar grammar, List<Gesture> gestures, List<PersonName> names, List<PredefinedQuestion> questions)
			: this()
		{
			if (grammar == null)
				throw new ArgumentNullException();
			this.grammar = grammar;
			this.gestures = (gestures != null) ? gestures : new List<Gesture>();
			this.names = (names != null) ? names : new List<PersonName>();
			this.questions = (questions != null) ? questions : new List<PredefinedQuestion>();

			this.rules_queue = new List<ProductionRule>();
			this.expandedRules = new HashSet<string>(StringComparer.InvariantCulture);
			this.expandedWildcards = new HashSet<string>(StringComparer.InvariantCulture);

			forbiddenCharacters = new HashSet<string>();
			forbiddenCharacters.Add("\'");
			
			quotationCharacters = new HashSet<string>();
			quotationCharacters.Add("?");
			quotationCharacters.Add(",");

		}


		/// <summary>
		/// Writes the provided message string and exception's Message to the console in RED text
		/// </summary>
		/// <param name="message">The message to be written.</param>
		/// <param name="ex">Exception to be written.</param>
		public static void Err(string message)
		{
			ConsoleColor pc = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Red;
			if (!String.IsNullOrEmpty(message))
				Console.WriteLine(message);
			Console.ForegroundColor = pc;
		}


		/// <summary>
		/// Saves the provided grammar as an SRGSS xml file
		/// </summary>
		/// <param name="grammar">The grammar to convert</param>
		/// <param name="filePath">Path to the SRGS xml file to save the grammar in</param>
		/// <param name="grammar">The grammar to convert</param>
		/// <param name="gestures">List of gesture names</param>
		/// <param name="names">List of people name</param>
		/// <param name="questions">List of known questions</param>
		public static void SaveToSRGS(Grammar grammar, string filePath, List<Gesture> gestures, List<PersonName> names, List<PredefinedQuestion> questions)
		{
			if (grammar == null)
				throw new ArgumentNullException();
			GrammarConverter converter = new GrammarConverter();
			converter.grammar = grammar;
			converter.gestures = gestures;
			converter.names = names;
			converter.questions = questions;

			converter.ConvertToXmlSRGS(filePath);
		}

		/// <summary>
		/// Converts the grammar to Xml SRGS specification, saving it in the provided stream
		/// </summary>
		public void ConvertToXmlSRGS(TextWriter writer)
		{
			XmlWriterSettings settings = new XmlWriterSettings();
			settings.Indent = true;
			settings.IndentChars = "\t";
			using (this.writer = XmlTextWriter.Create(writer, settings))
			{
				SRGSWriteHeader();
				SRGSWriteMainRule();
				foreach (var productionRule in grammar.ProductionRules.Values)
				{
					if (productionRule.NonTerminal == "$Main")
						continue;
					SRGSWriteProductionRule(productionRule);
				}
				SRGSWriteWildcardRules();
				SRGSWriteQuestionsRule();
				this.writer.WriteEndElement();
			}
		}

		/// <summary>
		/// Converts the grammar to BNF SRGS specification, saving it into the specified file
		/// </summary>
		/// <param name="filePath">The name of the file in which the converted grammar will be stored</param>
		public void ConvertToBnf(string filePath, bool addManualAnswers)
		{
			using (StreamWriter stream = new StreamWriter(filePath))
			{
				textWriter = stream;
				WriteBnf(addManualAnswers);
				stream.Close();
			}
		}

		/// <summary>
		/// Converts the grammar to BNF SRGS specification, saving it in the provided stream
		/// </summary>
		public void WriteBnf(bool addManualAnswers)
		{
			
			BNFWriteHeader();

			if (include_comments)
				FilteredWriteLine("/*\nMain Rule\n*/");
			
			BNFWriteMainRule(addManualAnswers);

			if (addManualAnswers)
			{
				if (include_comments)
					FilteredWriteLine("/*\nManually Added Rules\n*/");

				BNFWriteManualRule();
			}

			if (include_comments)
				FilteredWriteLine("/*\nProduction Rules\n*/");

			ProductionRule currentRule = grammar.ProductionRules["$Main"];
			rules_queue.Add(currentRule);

			while (rules_queue.Count > 0)
			{
				currentRule = rules_queue.PopLast();
				expandedRules.Add(currentRule.NonTerminal);

				foreach (var rule in currentRule.Replacements)
				{

					Tuple<List<string>,List<string>> nonTernimalAndWildcardReplacements = GetExpandedNonTerminals(rule);

					var nonTernimalReplacements = nonTernimalAndWildcardReplacements.Item1;
					var wildcardReplacements = nonTernimalAndWildcardReplacements.Item2;

					foreach (var wildcardReplacement in wildcardReplacements)
						if (wildcardReplacement != "")
							expandedWildcards.Add(wildcardReplacement);

					foreach (var nonTerminalReplacement in nonTernimalReplacements)
					{
						
						if (!grammar.ProductionRules.ContainsKey("$" + nonTerminalReplacement))
						{
							Err(string.Format("Non terminal replacement \"${0}\" for rule \"{1}\" not found in grammar rules! Grammar may be missing some rules. Please, manually compare with the command generator.", nonTerminalReplacement, rule));

							if (grammar.ProductionRules.ContainsKey(rule))
							{
								Err(string.Format("Incomplete rule: \"{0}\"", grammar.ProductionRules[rule]));
							}
							continue;
						}

						if (!expandedRules.Contains("$" + nonTerminalReplacement))
						{
							rules_queue.Add(grammar.ProductionRules["$" + nonTerminalReplacement]);
						}
					}

				}

			}

			foreach (var productionRuleName in expandedRules)
			{

				var productionRule = grammar.ProductionRules[productionRuleName];
				if (productionRuleName == "$Main")
					continue;
				BNFWriteProductionRule(productionRule);
			}

			if (include_comments)
				FilteredWriteLine("/*\nWildcard Rules\n*/");


			BNFWriteGesturesRules();
			BNFWriteLocationsRules();
			BNFWriteNamesRules();
			BNFWriteObjectsRules();
			BNFWritePronounsRules();

			if (include_comments)
				FilteredWriteLine("/*\nQuestions Rules\n*/");
			
			BNFWriteQuestionsRule();
			
		}

		protected virtual void FilteredWrite(string s)
		{
			string fileterString = s;

			foreach (string subString in forbiddenCharacters)
			{
				fileterString = fileterString.Replace(subString, "");
			}
			
			foreach (string subString in quotationCharacters)
			{
				fileterString = fileterString.Replace(subString, " \"" + subString + "\" ");
			}

			textWriter.Write(fileterString);

		}

		protected virtual void FilteredWriteLine(string s)
		{
			FilteredWrite(s + "\n");
		}

		protected virtual void BNFWriteHeader()
		{
			textWriter.WriteLine("#BNF+EMV1.1;\n");
			
			textWriter.WriteLine("!grammar {0};\n", grammar.Name.Replace(" ", "_"));
			
			textWriter.WriteLine("!start <commands>;\n");
		
		}

		private void BNFWriteManualRule()
		{
			
			FilteredWriteLine("<__answers> : <__name_answer> | <__object_answer> | wait here | wait | follow me;\n");

			FilteredWriteLine("<__name_answer>: my name is <_names> | I am <_names>;\n");
			expandedWildcards.Add("_names");

			FilteredWriteLine("<__object_answer>: The <_objects> would be great;\n");
			expandedWildcards.Add("_objects");

		}

		private void BNFWriteMainRule(bool addManualAnswers)
		{
			
			ProductionRule main = grammar.ProductionRules["$Main"];

			if (addManualAnswers)
			{
				FilteredWrite("<commands>: <main> | <__answers>;\n");
			
			}
			else
			{
				FilteredWrite("<commands>: <main>;\n");
			}

			FilteredWriteLine("<main>:");
			
			foreach (string replacement in main.Replacements)
			{
				BNFWriteReplacement(replacement);
				FilteredWriteLine("|");
			}
			
			BNFWriteRuleRef("_questions");
			FilteredWriteLine(";\n");
		
		}

		private void BNFWriteReplacement(string replacement)
		{
			BNFExpandReplacement(replacement);
		}

		private Tuple<List<string>,List<string>> GetExpandedNonTerminals(string replacement)
		{

			List<string> nonTerminals = new List<string>();
			List<string> wildcards = new List<string>();

			// Search in sentence for non-terminals
			int cc = 0;
			while (cc < replacement.Length)
			{
				if (replacement[cc] == '$')
				{
					nonTerminals.Add(GetExpandNonTerminal(replacement, ref cc));
				}
				else if (replacement[cc] == '{')
				{
					wildcards.Add(GetExpandWildcard(replacement, ref cc));
				}
				else
				{
					++cc;
				}
			}

			return new Tuple<List<string>,List<string>>(nonTerminals, wildcards);
		}

		private string GetExpandNonTerminal(string replacement, ref int cc)
		{
			string nonTerminal = FetchNonTerminal(replacement, ref cc);
			return BNFNonTerminalToRuleName(nonTerminal);
		}

		private void BNFExpandReplacement(string replacement)
		{
			// Search in sentence for non-terminals
			int cc = 0;
			while (cc < replacement.Length)
			{
				if (replacement[cc] == '$')
					BNFExpandNonTerminal(replacement, ref cc);
				else if (replacement[cc] == '{')
					BNFExpandWildcard(replacement, ref cc);
				else
				{
					// write everything else
					FilteredWrite(replacement.Substring(cc, 1));
					++cc;
				}
			}
		}

		private void BNFExpandNonTerminal(string replacement, ref int cc)
		{
			string nonTerminal = FetchNonTerminal(replacement, ref cc);
			string uri = BNFNonTerminalToRuleName(nonTerminal);
			BNFWriteRuleRef(uri);
		}

		private string BNFNonTerminalToRuleName(string nonTerminal)
		{
			return nonTerminal.Substring(1, 1) + (nonTerminal.Length > 2 ? nonTerminal.Substring(2) : String.Empty);
//			return nonTerminal.Substring(1, 1).ToLower() + (nonTerminal.Length > 2 ? nonTerminal.Substring(2) : String.Empty); // caused an error when getting rules like $Main_1
		}

		private void BNFWriteRuleRef(string uri)
		{
			textWriter.Write("<{0}>", uri);
		}

		private void BNFExpandWildcard(string s, ref int cc)
		{
			string uri = ""; 
			TextWildcard w = TextWildcard.XtractWildcard(s, ref cc);

			string keyword = ComputeKeyword(w);

			if (w.Obfuscated)
			{
				switch (keyword)
				{
					case "category":
						FilteredWrite("objects");
						break;
						
					case "room":
						FilteredWrite("room");
						break;

					case "question":
						FilteredWrite("question");
						break;

					case "void":
						break;

					case "location":
					case "beacon":
					case "placement":
						uri = "_rooms";
						break;

					case "object":
					case "aobject":
					case "kobject":
					case "sobject":
						uri = "_categories";
						break;

					case "pron":
						uri = "_pronobjs";
						break;

					case "gesture":
					case "name":
					case "female":
					case "male":
					case "pronobj":
					case "pronsub":
						uri = "_" + w.Name + "s";
						break;

					default:
						break;
				}

			}
			else
			{
				switch (keyword)
				{
					
					case "question":
						FilteredWrite("question");
						break;

					case "void":
						break;

					case "category":
						uri = "_categories";
						break;

					case "pron":
						uri = "_pronobjs";
						break;

					case "gesture":
					case "name":
					case "female":
					case "male":
					case "location":
					case "beacon":
					case "placement":
					case "room":
					case "object":
					case "aobject":
					case "kobject":
					case "sobject":
					case "pronobj":
					case "pronsub":
						uri = "_" + w.Name + "s";
						break;

					default:
						break;
				}

			}

			if (uri.Length > 0)
				BNFWriteRuleRef(uri);
			
		}

		private string GetExpandWildcard(string s, ref int cc)
		{

			string uri = ""; 
			TextWildcard w = TextWildcard.XtractWildcard(s, ref cc);

			string keyword = ComputeKeyword(w);

			if (w.Obfuscated)
			{
				switch (keyword)
				{
					case "category":
					case "room":
					case "question":
					case "void":
						break;

					case "location":
					case "beacon":
					case "placement":
						uri = "_rooms";
						break;

					case "object":
					case "aobject":
					case "kobject":
					case "sobject":
						uri = "_categories";
						break;

					case "pron":
						uri = "_pronobjs";
						break;

					case "gesture":
					case "name":
					case "female":
					case "male":
					case "pronobj":
					case "pronsub":
						uri = "_" + w.Name + "s";
						break;

					default:
						break;
				}

			}
			else
			{
				switch (keyword)
				{

					case "question":
					case "void":
						break;

					case "category":
						uri = "_categories";
						break;

					case "pron":
						uri = "_pronobjs";
						break;

					case "gesture":
					case "name":
					case "female":
					case "male":
					case "location":
					case "beacon":
					case "placement":
					case "room":
					case "object":
					case "aobject":
					case "kobject":
					case "sobject":
					case "pronobj":
					case "pronsub":
						uri = "_" + w.Name + "s";
						break;

					default:
						break;
				}

			}

			return uri;

		}

		private void BNFWriteProductionRule(ProductionRule productionRule)
		{
			List<string> validReplacements = GetValidReplacements(productionRule);
			
			FilteredWriteLine(String.Format("<{0}>:", BNFNonTerminalToRuleName(productionRule.NonTerminal)));

			BNFWriteReplacements(validReplacements);

		}

		private void BNFWriteReplacements(List<string> replacements)
		{
			for (int i = 0; i < replacements.Count; i++)
			{
				string replacement = replacements[i];

				BNFWriteReplacement(replacement);

				if (i < replacements.Count - 1)
					FilteredWriteLine("|");
			}

			FilteredWriteLine(";\n");

		}

		private void BNFWriteGesturesRules()
		{
			if (gestures == null)
				return;

			if (!expandedWildcards.Contains("_gestures"))
				return;
			
			FilteredWriteLine("<_gestures>:");
			
			foreach (Gesture gesture in gestures)
			{
				FilteredWrite(gesture.Name);
				FilteredWriteLine("|");
			}
			
			FilteredWriteLine(";\n");

		}

		private void BNFWriteLocationsRules()
		{
			if (locations == null)
				return;

			if (expandedWildcards.Contains("_locations"))
			{

				FilteredWriteLine("<_locations>:");
				
				BNFWriteRuleRef("_beacons");
				FilteredWriteLine("|");

				BNFWriteRuleRef("_placements");
				FilteredWriteLine("|");

				BNFWriteRuleRef("_rooms");
				FilteredWriteLine(";\n");

			}

		
			if (expandedWildcards.Contains("_locations") || expandedWildcards.Contains("_beacons"))
				BNFWriteBeaconsRule();
		
			if (expandedWildcards.Contains("_locations") || expandedWildcards.Contains("_placements"))
				BNFWritePlacementsRule();
		
			if (expandedWildcards.Contains("_locations") || expandedWildcards.Contains("_rooms"))
				BNFWriteRoomsRule();

		}

		private void BNFWriteBeaconsRule()
		{
			
			FilteredWriteLine("<_beacons>:");

			HashSet<string> hsLocations = new HashSet<string>();
			foreach (Location loc in locations)
			{

				if (loc.IsBeacon && !hsLocations.Contains(loc.Name))
				{
					hsLocations.Add(loc.Name);
				}
			}

			FilteredWriteLine(string.Join("|\n", hsLocations) + ";\n");

		}

		private void BNFWritePlacementsRule()
		{
			FilteredWriteLine("<_placements>:");
			
			HashSet<string> hsLocations = new HashSet<string>();
			foreach (Location loc in locations)
			{
				if (loc.IsPlacement && !hsLocations.Contains(loc.Name))
					hsLocations.Add(loc.Name);
			}

			foreach (Category category in objects.Categories)
			{
				if (category.DefaultLocation.IsPlacement && !hsLocations.Contains(category.DefaultLocation.Name))
					hsLocations.Add(category.DefaultLocation.Name);
			}

			FilteredWriteLine(string.Join("|\n", hsLocations) + ";\n");
			
		}

		private void BNFWriteRoomsRule()
		{
			FilteredWriteLine("<_rooms>:");

			HashSet<string> hsRooms = new HashSet<string>();
			foreach (Room room in locations.Rooms)
			{
				if (!hsRooms.Contains(room.Name))
					hsRooms.Add(room.Name);
			}
			foreach (Category category in objects.Categories)
			{
				if (!hsRooms.Contains(category.RoomString))
					hsRooms.Add(category.RoomString);
			}
			
			FilteredWrite(string.Join("|\n", hsRooms) + ";\n\n");
		
		}

		private void BNFWriteNamesRules()
		{
			if (names == null)
				return;
			
			if (expandedWildcards.Contains("_names"))
			{

				FilteredWriteLine("<_names>:");

				BNFWriteRuleRef("_males");
				FilteredWriteLine("|");

				BNFWriteRuleRef("_females");
				FilteredWriteLine(";\n");

			}


			List<string> maleNames = new List<string>();
			List<string> femaleNames = new List<string>();

			foreach (PersonName name in names)
			{
				switch (name.Gender)
				{
					case Gender.Male:
						maleNames.Add(name.Name);
						break;
					case Gender.Female:
						femaleNames.Add(name.Name);
						break;
					default:
						break;
				}

			}

			if (expandedWildcards.Contains("_names") || expandedWildcards.Contains("_males"))
			{

				FilteredWriteLine("<_males>:");

				FilteredWrite(string.Join("|\n", maleNames) + ";\n\n");

			}

			if (expandedWildcards.Contains("_names") || expandedWildcards.Contains("_females"))
			{

				FilteredWriteLine("<_females>:");

				FilteredWrite(string.Join("|\n", femaleNames) + ";\n\n");

			}
		
		}

		private void BNFWriteObjectsRules()
		{
			if (objects == null)
				return;
			
			if (expandedWildcards.Contains("_objects"))
			{
				
				FilteredWriteLine("<_objects>:");

				BNFWriteRuleRef("_aobjects");
				FilteredWriteLine("|");
		
				BNFWriteRuleRef("_kobjects");
				FilteredWriteLine("|");
		
				BNFWriteRuleRef("_sobjects");			
				FilteredWriteLine(";\n");

			}
			
			if (expandedWildcards.Contains("_objects") || expandedWildcards.Contains("_categories"))
				BNFWriteCategoriesRule();

			var aobjects = new List<string>();
			var kobjects = new List<string>();
			var sobjects = new List<string>();

			foreach (Object o in objects.Objects)
				switch (o.Type)
				{
					case ObjectType.Alike:
						aobjects.Add(o.Name);
						break;
					case ObjectType.Known:
						kobjects.Add(o.Name);
						break;
					case ObjectType.Special:
						sobjects.Add(o.Name);
						break;
					default:
						break;
				}


			if (expandedWildcards.Contains("_objects") || expandedWildcards.Contains("_aobjects"))
			{
				FilteredWriteLine("<_aobjects>:");
				FilteredWriteLine(string.Join("|\n", aobjects) + ";\n");
			}
			
			if (expandedWildcards.Contains("_objects") || expandedWildcards.Contains("_kobjects"))
			{
				FilteredWriteLine("<_kobjects>:");
				FilteredWriteLine(string.Join("|\n", kobjects) + ";\n");
			}
			
			if (expandedWildcards.Contains("_objects") || expandedWildcards.Contains("_sobjects"))
			{
				FilteredWriteLine("<_sobjects>:");
				FilteredWriteLine(string.Join("|\n", sobjects) + ";\n");
			}
			
		}

		private void BNFWriteCategoriesRule()
		{
			
			FilteredWriteLine("<_categories>:");

			for (int i = 0; i < objects.Categories.Count; i++)
			{
				var category = objects.Categories[i];

				FilteredWrite(category.Name);

				if (i < objects.Categories.Count - 1)
					FilteredWriteLine("|");
			}

			FilteredWriteLine(";\n");
		}

		private void BNFWritePronounsRules()
		{
			
			if (expandedWildcards.Contains("_pronobjs"))
			{
				FilteredWriteLine("<_pronobjs>:");
				FilteredWriteLine(string.Join("|\n", Pronoun.Personal.AllObjective) + ";\n");
			
			}
			
			if (expandedWildcards.Contains("_pronsubs"))
			{
				FilteredWriteLine("<_pronsubs>:");
				FilteredWriteLine(string.Join("|\n", Pronoun.Personal.AllSubjective) + ";\n");
			}
		
		}

		private void BNFWriteQuestionsRule()
		{
			FilteredWriteLine("<_questions>:");

			for (int i = 0; i < questions.Count; i++)
			{
				PredefinedQuestion question = questions[i];

				FilteredWrite(question.Question);
				
				if (i < questions.Count - 1)
					FilteredWriteLine("|");
			}

			FilteredWriteLine(";\n");
		}

		/// <summary>
		/// Converts the grammar to ABNF SRGS specification, saving it in the provided stream
		/// </summary>
		public void ConvertToAbnfSRGS(TextWriter writer)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Converts the grammar to ABNF SRGS specification, saving it into the specified file
		/// </summary>
		/// <param name="filePath">The name of the file in which the converted grammar will be stored</param>
		public void ConvertToAbnfSRGS(string filePath)
		{
			using (StreamWriter stream = new StreamWriter(filePath))
			{
				ConvertToAbnfSRGS(stream);
				stream.Close();
			}
		}

		/// <summary>
		/// Converts the grammar to Xml SRGS specification, saving it into the specified file
		/// </summary>
		/// <param name="filePath">The name of the file in which the converted grammar will be stored</param>
		public void ConvertToXmlSRGS(string filePath)
		{
			using (StreamWriter stream = new StreamWriter(filePath))
			{
				ConvertToXmlSRGS(stream);
				stream.Close();
			}
		}

		protected virtual void SRGSWriteHeader()
		{
			writer.WriteStartDocument();
			// writer.WriteDocType("grammar", "-//W3C//DTD GRAMMAR 1.0//EN", "http://www.w3.org/TR/speech-grammar/grammar.dtd", null);
			writer.WriteStartElement("grammar", "http://www.w3.org/2001/06/grammar");
			writer.WriteAttributeString("version", "1.0");
			writer.WriteAttributeString("xml", "lang", null, "en-US");
			// writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
			// writer.WriteAttributeString("xsi", "schemaLocation", null, "http://www.w3.org/2001/06/grammar http://www.w3.org/TR/speech-grammar/grammar.xsd");
			writer.WriteAttributeString("root", "main");


		}

		private string ComputeKeyword(TextWildcard w)
		{
			if (w.Type == null)
				return w.Name;

			switch (w.Name)
			{
				case "location":
					switch (w.Type)
					{
						case "beacon":
						case "placement":
						case "room":
							return w.Type;
					}
					break;

				case "name":
					switch (w.Type)
					{
						case "male":
						case "female":
							return w.Type;
					}
					break;

				case "object":
					switch (w.Type)
					{
						case "aobject":
						case "kobject":
						case "special":
							return w.Type[0].ToString() + w.Name;
					}
					break;

				case "pron":
					switch (w.Type)
					{
						case "obj":
							return "pronobj";
						case "sub":
							return "pronsub";
					}
					break;
			}

			return w.Name;
		}

		private void SRGSWriteMainRule()
		{
			ProductionRule main = grammar.ProductionRules["$Main"];
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "main");
			writer.WriteAttributeString("scope", "public");

			writer.WriteStartElement("one-of");
			foreach (string replacement in main.Replacements)
				SRGSWriteReplacement(replacement);
			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_questions");
			writer.WriteEndElement();
			writer.WriteEndElement();

			writer.WriteEndElement();
		}

		private void SRGSWriteProductionRule(ProductionRule productionRule)
		{
			List<string> validReplacements = GetValidReplacements(productionRule);
			
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", SRGSNonTerminalToRuleName(productionRule.NonTerminal));
			writer.WriteAttributeString("scope", "private");
			
			SRGSWriteReplacements(validReplacements);
			
			writer.WriteEndElement();
		}

		private List<string> GetValidReplacements(ProductionRule productionRule)
		{
			List<string> vr = new List<string>(productionRule.Replacements.Count);

			for (int i = 0; i < productionRule.Replacements.Count; ++i)
			{
				string replacement = (productionRule.Replacements[i] ?? String.Empty).Trim();

				for (int cc = 0; cc < replacement.Length; ++cc)
				{
					cc = replacement.IndexOf('{', cc);
					if (cc == -1)
						break;
					int bcc = cc;
					TextWildcard w = TextWildcard.XtractWildcard(replacement, ref cc);
					if (w.Name != "void")
						continue;
					replacement = replacement.Remove(bcc) + replacement.Substring(cc);
					cc = bcc;
				}

				if (String.IsNullOrEmpty(replacement))
					continue;

				vr.Add(replacement);
			}
			return vr;
		}

		private void SRGSWriteWildcardRules()
		{
			// SRGSWriteCategoriesRules();
			SRGSWriteGesturesRules();
			SRGSWriteLocationsRules();
			SRGSWriteNamesRules();
			SRGSWriteObjectsRules();
			SRGSWritePronounsRules();
		}

		private void SRGSWriteReplacements(List<string> replacements)
		{
			if (replacements.Count == 0)
			{
				writer.WriteStartElement("item");
				writer.WriteEndElement();
			}
			else if (replacements.Count == 1)
				SRGSWriteReplacement(replacements[0]);
			else
			{
				writer.WriteStartElement("one-of");
				foreach (string replacement in replacements)
					SRGSWriteReplacement(replacement);
				writer.WriteEndElement();
			}
		}

		private void SRGSWriteReplacement(string replacement)
		{
			writer.WriteStartElement("item");
			SRGSExpandReplacement(replacement);
			writer.WriteEndElement();
		}

		private void SRGSExpandReplacement(string replacement)
		{
			// Search in sentence for non-terminals
			int cc = 0;
			while (cc < replacement.Length)
			{
				if (replacement[cc] == '$')
					SRGSExpandNonTerminal(replacement, ref cc);
				else if (replacement[cc] == '{')
					SRGSExpandWildcard(replacement, ref cc);
				else
				{
					writer.WriteString(replacement.Substring(cc, 1));
					++cc;
				}
			}
		}

		private void SRGSWriteRuleRef(string uri)
		{
			writer.WriteStartElement("ruleref");
			writer.WriteAttributeString("uri", uri);
			writer.WriteEndElement();
		}

		private void SRGSWriteItem(string value)
		{
			writer.WriteStartElement("item");
			writer.WriteString(value);
			writer.WriteEndElement();
		}

		/*
		private void SRGSExpandWhereWildcard(TextWildcard w)
		{
			string keyword = ComputeKeyword(w);
			string uri = "#_";
			SRGSWriteRuleRef(uri);
		}
		*/

		private void SRGSExpandWildcard(string s, ref int cc)
		{
			string uri = "#_";
			TextWildcard w = TextWildcard.XtractWildcard(s, ref cc);
			/*
			if (!String.IsNullOrEmpty(w.Where))
			{
				SRGSExpandWhereWildcard(w);
				return;
			}
			*/
			string keyword = ComputeKeyword(w);
			switch (keyword)
			{
				case "category":
					uri += "categories";
					break;

				case "pron":
					uri += "pronobjs";
					break;

				case "gesture":
				case "name":
				case "female":
				case "male":
				case "location":
				case "beacon":
				case "placement":
				case "room":
				case "object":
				case "aobject":
				case "kobject":
				case "sobject":
				case "pronobj":
				case "pronsub":
					uri += w.Name + "s";
					break;

				case "question":
					writer.WriteString("question");
					return;

				case "void":
					return;

				default:
					return;
			}
			SRGSWriteRuleRef(uri);
		}

		private void SRGSExpandNonTerminal(string replacement, ref int cc)
		{
			string nonTerminal = FetchNonTerminal(replacement, ref cc);
			string uri = "#" + SRGSNonTerminalToRuleName(nonTerminal);
			SRGSWriteRuleRef(uri);
		}

		private string FetchNonTerminal(string s, ref int cc)
		{
			char c;
			int bcc = cc++;
			while (cc < s.Length)
			{
				c = s[cc];
				if (((c >= '0') && (c <= '9')) || ((c >= 'A') && (c <= 'Z')) || ((c >= 'a') && (c <= 'z')) || (c == '_'))
					++cc;
				else
					break;
			}
			return s.Substring(bcc, cc - bcc);
		}

		private string SRGSNonTerminalToRuleName(string nonTerminal)
		{
			return nonTerminal.Substring(1, 1).ToLower() + (nonTerminal.Length > 2 ? nonTerminal.Substring(2) : String.Empty);
		}

		private void SRGSWriteGesturesRules()
		{
			if (gestures == null)
				return;
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_gestures");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (Gesture gesture in gestures)
			{
				SRGSWriteItem(gesture.Name);
			}
			writer.WriteEndElement();
			writer.WriteEndElement();
		}

		private void SRGSWriteLocationsRules()
		{
			if (locations == null)
				return;
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_locations");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_beacons");
			writer.WriteEndElement();

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_placements");
			writer.WriteEndElement();

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_rooms");
			writer.WriteEndElement();

			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>

			SRGSWriteBeaconsRule();
			SRGSWritePlacementsRule();
			SRGSWriteRoomsRule();
		}

		private void SRGSWriteBeaconsRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_beacons");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			HashSet<string> hsLocations = new HashSet<string>();
			foreach (Location loc in locations)
			{
				if (loc.IsBeacon && !hsLocations.Contains(loc.Name))
					SRGSWriteItem(loc.Name);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWritePlacementsRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_placements");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			HashSet<string> hsLocations = new HashSet<string>();
			foreach (Location loc in locations)
			{
				if (loc.IsPlacement && !hsLocations.Contains(loc.Name))
					hsLocations.Add(loc.Name);
			}
			foreach (Category category in objects.Categories)
			{
				if (category.DefaultLocation.IsPlacement && !hsLocations.Contains(category.DefaultLocation.Name))
					hsLocations.Add(category.DefaultLocation.Name);
			}
			foreach (string loc in hsLocations)
			{
				SRGSWriteItem(loc);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteRoomsRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_rooms");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (Room room in locations.Rooms)
			{
				SRGSWriteItem(room.Name);
			}

			HashSet<string> hsRooms = new HashSet<string>();
			foreach (Room room in locations.Rooms)
			{
				if (!hsRooms.Contains(room.Name))
					hsRooms.Add(room.Name);
			}
			foreach (Category category in objects.Categories)
			{
				if (!hsRooms.Contains(category.RoomString))
					hsRooms.Add(category.RoomString);
			}
			foreach (string room in hsRooms)
			{
				SRGSWriteItem(room);
			}

			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteNamesRules()
		{
			if (names == null)
				return;
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_names");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_males");
			writer.WriteEndElement();

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_females");
			writer.WriteEndElement();

			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>

			SRGSWriteMaleNamesRule();
			SRGSWriteFemaleNamesRule();
		}

		private void SRGSWriteFemaleNamesRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_females");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (PersonName name in names)
			{
				if (name.Gender != Gender.Female)
					continue;
				SRGSWriteItem(name.Name);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteMaleNamesRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_males");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (PersonName name in names)
			{
				if (name.Gender != Gender.Male)
					continue;
				SRGSWriteItem(name.Name);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteObjectsRules()
		{
			if (objects == null)
				return;

			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_objects");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_aobjects");
			writer.WriteEndElement();

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_kobjects");
			writer.WriteEndElement();

			writer.WriteStartElement("item");
			SRGSWriteRuleRef("#_sobjects");
			writer.WriteEndElement();

			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>

			SRGSWriteCategoriesRule();
			SRGSWriteAObjectsRule();
			SRGSWriteKObjectsRule();
			SRGSWriteSObjectsRule();
		}

		private void SRGSWriteCategoriesRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_categories");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (Category category in objects.Categories)
			{
				SRGSWriteItem(category.Name);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteAObjectsRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_aobjects");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (Object o in objects.Objects)
			{
				if (o.Type != ObjectType.Alike)
					continue;
				SRGSWriteItem(o.Name);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteKObjectsRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_kobjects");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (Object o in objects.Objects)
			{
				if (o.Type != ObjectType.Known)
					continue;
				SRGSWriteItem(o.Name);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteSObjectsRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_sobjects");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (Object o in objects.Objects)
			{
				if (o.Type != ObjectType.Special)
					continue;
				SRGSWriteItem(o.Name);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWritePronounsRules()
		{
			SRGSWritePronouns_Obj_Rule();
			SRGSWritePronouns_Sub_Rule();
		}

		private void SRGSWritePronouns_Obj_Rule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_pronobjs");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (string s in Pronoun.Personal.AllObjective)
				SRGSWriteItem(s);
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWritePronouns_Sub_Rule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_pronsubs");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (string s in Pronoun.Personal.AllSubjective)
				SRGSWriteItem(s);
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}

		private void SRGSWriteQuestionsRule()
		{
			writer.WriteStartElement("rule");
			writer.WriteAttributeString("id", "_questions");
			writer.WriteAttributeString("scope", "private");
			writer.WriteStartElement("one-of");
			foreach (PredefinedQuestion question in questions)
			{
				SRGSWriteItem(question.Question);
			}
			writer.WriteEndElement(); // </one-of>
			writer.WriteEndElement(); // </rule>
		}
	}
}
