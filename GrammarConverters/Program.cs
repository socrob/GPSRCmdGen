using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RoboCup.AtHome.CommandGenerator;
using RoboCup.AtHome.CommandGenerator.Containers;
using RoboCup.AtHome.CommandGenerator.ReplaceableTypes;
using RoboCup.AtHome.GPSRCmdGen;
using RoboCup.AtHome.EEGPSRCmdGen;

namespace RoboCup.AtHome.GrammarConverters
{
	
	/// <summary>
	/// Exports the specified grammar to the supported specification formats
	/// </summary>
	public class GrammarExporter : RoboCup.AtHome.CommandGenerator.Generator
	{
		#region Variables

		bool alwaysOverwrite = false;
		string grammarsFolder = "";

		const string GRAMMAR_OPTION = "from=";

		#endregion

		#region Constructor

		/// <summary>
		/// Initializes a new instance of GrammarExporter
		/// </summary>
		public GrammarExporter()
			: base()
		{
		}

		#endregion

		#region Properties

		#endregion

		#region Static Methods

		/// <summary>
		/// The entry point of the program, where the program control starts and ends.
		/// </summary>
		/// <param name="args">The command-line arguments.</param>
		public static void Main(string[] args)
		{
			
			string outputFilename = "";
			string grammarsFrom = "gpsr";
			bool alwaysOverwrite = false;
			bool invalidArgs = false;

			Console.WriteLine("Note: at the moment only BNF output format is available.");

			foreach (var s in args)
			{
				string option = "";
				if (s.Substring(0, 2) == "--")
				{
					option = s.Substring(2);
				}
				else if (s.Substring(0, 1) == "-")
				{
					option = s.Substring(1);
				}
				else
				{
					if (outputFilename.Length > 0)
						invalidArgs = true;
					
					outputFilename = s;
				}

				if (option == "overwrite" || option == "w")
				{
					alwaysOverwrite = true;
				}

				try
				{
					if (option.Substring(0, GRAMMAR_OPTION.Length) == GRAMMAR_OPTION)
					{
						grammarsFrom = option.Substring(GRAMMAR_OPTION.Length).ToLower();
					}
				}
				catch (System.ArgumentOutOfRangeException)
				{
				}

			}

			if (invalidArgs || args.Length < 1 || outputFilename.Length == 0)
			{
				Green("Usage: ./GrammarConverter.exe output_path [-w|--overwrite] [--from=(gpsr|eegpsr|spr)]" +
					"\nOne file for each available grammar is exported, the grammar name is added to output_path." +
					"\nWhen -w and --overwrite options are present, files will be overwritten without asking." +
					"\nSpecify --from=(gpsr|eegpsr|spr) to select which grammars to export. Default: gpsr." +
					"\n    example: ./GrammarConverters.exe ~/robocup_2056 -w --from=gpsr" +
					"\n    will export robocup_2056_Category_I.bnf, ..., robocup_2056_Category_III.bnf");
				return;
			}

			string grammars_folder = grammarsFrom + "_grammars";

			Console.WriteLine(grammars_folder);
			InitializePath(grammars_folder);

			bool addManualAnswers = grammarsFrom != "spr";

			try
			{
				new GrammarExporter().Export(grammars_folder, outputFilename, alwaysOverwrite, addManualAnswers);
			}
			catch
			{
				Err("Failed! Application terminated");
				Environment.Exit(0);
			}
		}

		/// <summary>
		/// Checks if at least one of the required files are present. If not, initializes the
		/// directory with example files
		/// </summary>
		public static void InitializePath(string grammars_folder)
		{
			int xmlFilesCnt = System.IO.Directory.GetFiles(Loader.ExePath, "*.xml", System.IO.SearchOption.TopDirectoryOnly).Length;

			switch (grammars_folder)
			{
				case "eegpsr_grammars":
					if ((xmlFilesCnt < 4) || !System.IO.Directory.Exists(Loader.GetPath(grammars_folder)))
						RoboCup.AtHome.EEGPSRCmdGen.ExampleFilesGenerator.GenerateExampleFiles();
					break;
				case "gpsr_grammars":
					if ((xmlFilesCnt < 4) || !System.IO.Directory.Exists(Loader.GetPath(grammars_folder)))
						RoboCup.AtHome.GPSRCmdGen.ExampleFilesGenerator.GenerateExampleFiles();
					break;
				case "spr_grammars":
					if ((xmlFilesCnt < 4) || !System.IO.Directory.Exists(Loader.GetPath(grammars_folder)))
						RoboCup.AtHome.SPRTest.ExampleFilesGenerator.GenerateExampleFiles();
					break;
				default:
					Err("Unknown grammars folder [{0}]", grammars_folder);
					Environment.Exit(0);
					break;
			}
		}

		/// <summary>
		/// Queries the user for file overwrite permission
		/// </summary>
		/// <param name="file">The name of the file which will be overwritten</param>
		/// <returns><c>true</c> if the user authorizes the overwrite, otherwise <c>false</c></returns>
		private bool CanOverwrite(string full_path)
		{
			if (this.alwaysOverwrite || !File.Exists(full_path))
				return true;
			
			Console.Write("File {0} already exists. Overwrite? [yN] ", full_path);
			string answer = Console.ReadLine().ToLower();
			if ((answer == "y") || (answer == "yes"))
			{
				return true;
			}

			return false;
		}

		#endregion

		#region Load Methods

		protected void Export(string grammarsFolder, string outputFilename, bool alwaysOverwrite, bool addManualAnswers)
		{
			this.alwaysOverwrite = alwaysOverwrite;
			this.grammarsFolder = grammarsFolder;

			LoadData();

			foreach (var g in allGrammars)
			{

				Console.WriteLine("Loaded grammar {0} (difficulty {1})", g.Name, g.Tier.ToString());

				string fullPath = "";

				try
				{
					string filename = Path.GetFileNameWithoutExtension(outputFilename) + string.Format("_{0}.bnf", g.Name.Replace(" ", "_"));
					fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(outputFilename), filename));
				}
				catch
				{
					Err("Failed creating a path for grammar {0}. Skipping.", g.Name);
					continue;
				}

				GrammarConverter converter = new GrammarConverter(g, allGestures, allNames, allQuestions);

				if (CanOverwrite(fullPath))
				{
					Console.Write("Exporting grammar {0} (difficulty {1}) to {2}...", g.Name, g.Tier.ToString(), fullPath);
					converter.ConvertToBnf(fullPath, addManualAnswers);
					Green("Done!");
				}
				else
				{
					Green("Skipped.");
				}
			}

		}


		protected void LoadData()
		{
			Console.Write("Loading objects...");
			this.LoadObjects();
			Console.Write("Loading names...");
			this.LoadNames();
			Console.Write("Loading locations...");
			this.LoadLocations();
			Console.Write("Loading gestures...");
			this.LoadGestures();
			Console.Write("Loading predefined questions...");
			this.LoadQuestions();
			Console.Write("Loading grammars...");
			this.LoadGrammars();
			this.ValidateLocations();
		}

		/// <summary>
		/// Loads the grammars from disk. If no grammars are found, the application is
		/// terminated.
		/// </summary>
		public override void LoadGrammars()
		{
			try
			{
				this.allGrammars = Loader.LoadGrammars(this.grammarsFolder);
				Green("Done!");
			}
			catch
			{
				Err("Failed! Application terminated");
				Environment.Exit(0);
			}
		}

		/// <summary>
		/// Loads the set of gestures from disk. If no gestures file is found, 
		/// the default set is loaded from Resources
		/// </summary>
		public override void LoadGestures()
		{
			try
			{
				this.allGestures = Loader.Load<GestureContainer>(Loader.GetPath("Gestures.xml")).Gestures;
				Green("Done!");
			}
			catch
			{
				this.allGestures = Loader.LoadXmlString<GestureContainer>(Resources.Gestures).Gestures;
				Err("Failed! Default Gestures loaded");
			}
		}

		/// <summary>
		/// Loads the set of locations from disk. If no locations file is found, 
		/// the default set is loaded from Resources
		/// </summary>
		public override void LoadLocations()
		{
			try
			{
				this.allLocations = Loader.LoadLocations(Loader.GetPath("Locations.xml"));
				Green("Done!");
			}
			catch
			{
				List<Room> defaultRooms = Loader.LoadXmlString<RoomContainer>(Resources.Locations).Rooms;
				foreach (Room room in defaultRooms)
					this.allLocations.Add(room); 
				Err("Failed! Default Locations loaded");
			}
		}

		/// <summary>
		/// Loads the set of names from disk. If no names file is found, 
		/// the default set is loaded from Resources
		/// </summary>
		public override void LoadNames()
		{
			try
			{
				this.allNames = Loader.Load<NameContainer>(Loader.GetPath("Names.xml")).Names;
				Green("Done!");
			}
			catch
			{
				this.allNames = Loader.LoadXmlString<NameContainer>(Resources.Names).Names;
				Err("Failed! Default Names loaded");
			}
		}

		/// <summary>
		/// Loads the set of objects and categories from disk. If no objects file is found, 
		/// the default set is loaded from Resources
		/// </summary>
		public override void LoadObjects()
		{
			try
			{
				this.allObjects = Loader.LoadObjects(Loader.GetPath("Objects.xml"));
				Green("Done!");
			}
			catch
			{
				List<Category> defaultCategories = Loader.LoadXmlString<CategoryContainer>(Resources.Objects).Categories;
				foreach (Category category in defaultCategories)
					this.allObjects.Add(category);
				Err("Failed! Default Objects loaded");
			}
		}

		/// <summary>
		/// Loads the set of questions from disk. If no questions file is found, 
		/// the default set is loaded from Resources
		/// </summary>
		public override void LoadQuestions()
		{
			try
			{
				this.allQuestions = Loader.Load<QuestionsContainer>(Loader.GetPath("Questions.xml")).Questions;
				Green("Done!");
			}
			catch
			{
				this.allQuestions = Loader.LoadXmlString<QuestionsContainer>(Resources.Questions).Questions;
				Err("Failed! Default Objects loaded");
			}
		}

		#endregion

	}
}

