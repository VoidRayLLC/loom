using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Loom
{
	/// <summary>
	/// Static class to hold main entry point
	/// </summary>
	class Program
	{
		// Holds the options for this application
		static Options options = null;

		/// <summary>
		/// Main entry point for a C# application, and for ours
		/// </summary>
		/// <param name="argv">Any arguments passed into the application from the command line</param>
		/// <returns>0 on success, >0 otherwise</returns>
		static int Main(string[] argv)
		{
			#region Configure allowed options
			// Parse the command line arguments
			options = new Options(
				// --append
				new Option("append")
				{
					DefaultValue = false,
					HelpText = "If true, the output file will be appended to. Otherwise, it will be truncated and replaced (by appending)."
				},
				// -a <args> arguments for script 
				new Option("args")
				{
					ShortOption = 'a',
					ValuePresence = Option.ValueEnum.Required,
					HelpText = "Arguments for the script"
				},
				new Option("dry-run")
				{
					ShortOption = 'm',
					HelpText = "Print each command as if it were to be run, but do not run them. Useful to preview the result of a command without doing damage"
				},
				new Option("help")
				{
					ShortOption = 'h',
					HelpText = "Show this usage information",
					Callback = (o, a) => { o.ShowHelp(); o["help"].Value = true; }
				},
				// -n <#> number of threads. default=4 
				new Option("threads")
				{
					ShortOption = 'n',
					ValuePresence = Option.ValueEnum.Required,
					DefaultValue = 4,
					HelpText = "Limit number of concurrent threads to <n>"
				},
				// -o <output file> csv format. default=results.csv
				new Option("output-file")
				{
					ShortOption = 'o',
					ValuePresence = Option.ValueEnum.Optional,
					DefaultValue = "results.csv",
					HelpText = "Script to be called"
				},
				// -s <script> script to be called (powershell or batch) 
				new Option("script")
				{
					ShortOption = 's',
					ValuePresence = Option.ValueEnum.Required,
					HelpText = "Script to be called"
				},
				// -t <target file> list of IP or Hostnames. default=targets.txt 
				new Option("target")
				{
					ShortOption = 't',
					ValuePresence = Option.ValueEnum.Optional,
					DefaultValue = "targets.txt",
					HelpText = "File containing a list of IP or Hostnames"
				},
				new Option("verbose") 
				{
					ShortOption = 'v',
					HelpText = "Say everything we're doing"
				}
				);
			#endregion

			// Parse the options from argv
			options.Parse(argv);
			// If help requested, then bail (but with a success code)
			if (options["help"]) return 0;
			// Don't continue if something is wrong with the options
			if (options.Failed) return 1;

			#region Parse targets
			// Cache the targets string
			String targetsFile = options["target"];
			// Initialize our target list
			Queue<String> targets = new Queue<string>();

			// If a targets file was specified
			if (targetsFile != null)
			{
				// Error if the targets file doesn't exist
				if (!File.Exists(targetsFile))
				{
					// Tell the user that the file couldn't be found
					System.Console.WriteLine("ERROR: File {0} does not exist", targetsFile);
					// Bail with an error level
					return 1;
				}

				// Target file exists, parse it
				else
				{
					// Initialize a hashtable to utilize a a mechanism to remove duplicates
					Dictionary<String, String> deduper = new Dictionary<string, string>();
					// Iterate the whole file, deduping and adding to the target list
					foreach (String line in File.ReadAllLines(targetsFile))
						// Skip empty lines
						if (line.Trim() != "")
							// Store the line with the uppercase version as the key
							deduper[line.ToUpper().Trim()] = line.Trim();
					// Take our sanitized list and update the targets
					targets = new Queue<string>(deduper.Values);
				}
			}
			#endregion

			// Prepare the arguments
			List<String> arguments = new List<string>();
			// Add the only argument (later we're going to support a list)
			if (options["args"]) arguments.Add(options["args"]);
			// Add an empty target
			arguments.Add("");
			// Will we be doing a dry run?
			Boolean dryRun = options["dry-run"];

			#region Set the cap on the ThreadPool
			// To hold the number of threads 
			int maxThreads = options["threads"];
			// Limit the threads to 'n' threads
			if (maxThreads > 0) ThreadPool.SetMaxThreads(maxThreads, 0);
			#endregion

			// List of threads
			List<WaitCallback> tasks = new List<WaitCallback>();
			// ThreadPool won't tell us when all the threads are done, so making a 
			// manual reset event allows us to control when the ThreadPool should 
			// consider all the threads complete.
			ManualResetEvent backgroundThread = new ManualResetEvent(false);
			// Make sure outputFile isn't blank
			if (options["output-file"] == "") 
				// Set it back to the default
				options["output-file"].Value = options["output-file"].DefaultValue;
			// Get the output file we're supposed to write 
			String outputFile = options["output-file"];
			// Truncate the output file unless append is provided
			if (!options["append"]) File.Create(outputFile).Close();
			// Open the output file for writing the results
			FileStream file = File.Open(outputFile, FileMode.Append);

			// Execute the threads on all the targets
			foreach (String target in targets)
			{
				// Need to separate declaration and instantiation for recursive self-reference
				WaitCallback task = null;

				// Make a task out of the subscript
				task = (o) =>
				{
					// Set the target
					arguments[arguments.Count - 1] = target;
					// Run the command
					String result = RunCommand(dryRun, options["script"], arguments.ToArray());
					
					// Aquire a lock for the file to prevent simultaneous edit
					lock (file)
					{
						// Add this line to the end of the file
						file.Write(Encoding.ASCII.GetBytes(result), 0, result.Length);
						// Flush the output so anything tailing it can see it immediately
						file.Flush();
					}
					
					// Remove this task from the list
					tasks.Remove(task);
					// If the tasks are empty, then we're done
					if (tasks.Count == 0) backgroundThread.Set();
				};

				// Schedule a task for this item to put it into the ThreadPool
				ThreadPool.QueueUserWorkItem(task);
				// Add this task to the list of things for which to wait
				tasks.Add(task);
			}

			// Wait for the background thread to be set (complete)
			backgroundThread.WaitOne();
			// Close the file. Commit any buffered writes.
			file.Close();
			// Everything is ok. Return 0 (true)
			return 0;
		}

		/// <summary>
		/// Prepare and run the specified command with the specified arguments.
		/// 
		/// This method will prepare the arguments to the best of its ability, that
		/// can be difficult as it has no way of knowing the environment into which
		/// those arguments will be injected.
		/// 
		/// This method will not run the command if dryRun is true, but will still 
		/// prepare and echo the command.
		/// </summary>
		/// <param name="dryRun">True if you want to see the command without running it.</param>
		/// <param name="command">The command to run</param>
		/// <param name="arguments">The arguments to pass to the command</param>
		/// <returns>The captured STDOUT</returns>
		protected static String RunCommand(Boolean dryRun, String command, params String[] arguments)
		{
			// Prepare the arguments. For now just join them, but we'll have to consider escaping in the future
			String preparedArguments = String.Join(" ", arguments);
			// Dry run, just echo the command
			if(options["verbose"]) 
				System.Console.WriteLine("{0} {1}", command, String.Join(" ", arguments));

			// Don't actually spawn the process if this is a dry run
			if (!dryRun)
			{
				// Create a new process
				Process process = Process.Start(new ProcessStartInfo()
				{
					// Store the arguments
					Arguments = String.Join(" ", arguments),
					// Set the command
					FileName = command,
					// Don't use a shell (with an environment) to start the process
					UseShellExecute = false,
					// Capture STDOUT
					RedirectStandardOutput = true,
					// Capture STDERR
					RedirectStandardError = true,
				});

				// Buffer to hold the text from STDOUT
				String result = "";
				// Start capturing STDOUT
				process.BeginOutputReadLine();
				// Start capturing STDERR
				process.BeginErrorReadLine();

				// Handler for when we get errors
				process.ErrorDataReceived += (sender, eventData) =>
				{
					// Output the error information immediataely
					if(eventData.Data != null) System.Console.WriteLine(eventData.Data);
				};

				// Handler for when we get STDOUT data
				process.OutputDataReceived += (sender, eventData) =>
				{
					// Add this to the result
					result += eventData.Data + "\n";
				};

				// Wait for the process to cleanly exit
				process.WaitForExit();

				// Trim up the result to kill trailing whitespace
				return result.Trim();
			}

			return "";
		}
	}
}
