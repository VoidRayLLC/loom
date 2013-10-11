using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Loom
{
	/// <summary>
	/// Class for parsing command line arguments
	/// </summary>
	class Options
	{
		#region Fields
		/// <summary>
		/// True if the parsing did not succeed in such a manner that should 
		/// recovery should not be attempted
		/// </summary>
		public Boolean Failed;
		/// <summary>
		/// Callback for when an invalid argument is found. Assign your own closure 
		/// to this if you want to suppress or change the message.
		/// </summary>
		public Action<String> InvalidArgumentError;
		/// <summary>
		/// Prefixes for long arguments such as '--help'
		/// </summary>
		public String[] LongPrefixes = { "--" };
		/// <summary>
		/// List of parameters left over after parsing the options
		/// </summary>
		public List<string> Parameters;
		/// <summary>
		/// Prefixes that can signal incoming short arguments. 
		/// A prefix of '-' would allow an argument of '-h'.
		/// </summary>
		public Char[] ShortPrefixes = { '-' };
		/// <summary>
		/// Error called when an argument is present which requires a value.
		/// 
		/// Replace this closure to supress or change the error message.
		/// </summary>
		public Action<Option> ValueRequiredError;
		/// <summary>
		/// List of options that are accepted for this parser
		/// </summary>
		private List<Option> options = new List<Option>();
		#endregion

		#region Methods

		private String NextValue(Queue<String> arguments)
		{
			// Go to the next item
			if (arguments.Count > 0)
			{
				// Get the current enumerator item
				String value = arguments.Peek();
				// Special case for '-'
				if (value == "-") return arguments.Dequeue();
				// If this has a '-' in front of it, then it's invalid, because that's only for arguments
				else if (value.StartsWith("-")) return null;
				// Finally, if we're here, then just return the value
				else return arguments.Dequeue().Replace(@"\-", "-");
			}

			// Next item doesn't exist, so there is no value for this option
			else return null;
		}

		/// <summary>
		/// Create a new options instance and parse the arguments in argv
		/// </summary>
		/// <param name="options">The options that will be accepted for this parser</param>
		public Options(params Option[] options)
		{
			// Store the options for when Parse is called
			this.options.AddRange(options);

			// Setup the invalid argument handler
			this.InvalidArgumentError = argument =>
			{
				// Tell the user this argument is wrong
				System.Console.WriteLine("ERROR: Invalid argument: {0}", argument);
				// Show the basic usage information
				ShowUsage();
			};

			// Setup the value required error
			this.ValueRequiredError = argument =>
			{
				// Tell the user about the error
				System.Console.WriteLine("Value required for option: {0}", argument.LongOption);
			};
		}

		/// <summary>
		/// Parse all the arguments in argv, returning unused arguments as potential file names
		/// </summary>
		/// <param name="argv">The arguments to parse into the option data</param>
		/// <returns>Any unused options</returns>
		public String[] Parse(String[] argv)
		{
			// Set failed to true until this method exits
			Failed = true;
			// The unprocessed parameters
			Parameters = new List<string>();
			// Convert the arguments into a queue
			Queue<String> arguments = new Queue<string>(argv);

			// Go through all the arguments and check for options
			while (arguments.Count > 0)
			{
				// Get the top argument
				String argument = arguments.Dequeue();

				#region Long argument
				// This argument is a long argument
				if (argument.StartsWith("--"))
				{
					// Strip the '--' from the argument
					argument = argument.Substring(2);
					// Handle empty argument. If you put ' -- ' anywhere in the command line, 
					// then it means to stop argument processing.
					if (argument == "")
						// Add the remaining arguments to the list
						while (arguments.Count > 0) Parameters.Add(arguments.Dequeue());
					// Find the option to which this argument pertains
					Option option = options.Find(o => { return o.LongOption == argument; });

					// Handle option not found
					if (option == null)
					{
						// Document the invalid argument
						InvalidArgumentError(argument);
						// Returning null means that something was irreparably wrong with the arguments
						return null;
					}

					// Finally if we get here, then the argument is good
					else
					{
						if (option.Callback != null) option.Callback(this, arguments);

						else
							// See if there will be a value for this option
							switch (option.ValuePresence)
							{
								case Option.ValueEnum.Prohibited:
									// If values are prohibited, the consider this a boolean option
									option.Value = true;
									break;

								case Option.ValueEnum.Optional:
									// Get the next value into the option (null if not found)
									option.Value = NextValue(arguments) ?? option.DefaultValue;
									break;
								case Option.ValueEnum.Required:
									// Get the next value into the option
									option.Value = NextValue(arguments);
									// Throw the value required error, because at this point we're past 
									// the value and into the next argument
									if (option.Value == null) ValueRequiredError(option);
									break;
							}
					}
				}
				#endregion

				#region Short argument
				// Short argument
				else if (argument.StartsWith("-"))
				{
					// If the argument is too short, then we have a problem
					if (argument.Length < 2)
					{
						System.Console.WriteLine("argument: {0}", argument);
						// Throw the invalid argument error
						InvalidArgumentError(argument);
						// Return null for error
						return null;
					}

					// Remove the dash from the argument
					argument = argument.Substring(1);

					while (argument.Length > 0)
					{
						// Find the option that coencides with this argument
						Option option = options.Find(o => { return o.ShortOption == argument[0]; });

						// If we couldn't find the option, then throw an invalid argument error
						if (option == null)
						{
							// Throw the error
							InvalidArgumentError(argument);
							// Return null for irreparable error
							return null;
						}

						// Get the value
						else
						{
							// Remove the first letter from the argument
							argument = argument.Substring(1);
							// Handle callback
							if (option.Callback != null) option.Callback(this, arguments);

							else
								// See what kind of value this option may hold
								switch (option.ValuePresence)
								{
									#region Optional value
									case Option.ValueEnum.Optional:
										// If the argument has things after it, then use that for the value
										if (argument.Length > 0)
										{
											// Use the remainder for the argument
											option.Value = argument;
											// Kill the argument to prevent further chewing
											argument = "";
										}

										// Get the value into the option
										else option.Value = NextValue(arguments) ?? option.DefaultValue;
										break;
									#endregion

									#region Option that doesn't take a value
									case Option.ValueEnum.Prohibited:
										// If there is no value, then the option should just be set to true
										option.Value = true;
										break;
									#endregion

									#region Required value
									case Option.ValueEnum.Required:
										// If the argument has things after it, then use that for the value
										if (argument.Length > 0)
										{
											// Use the remainder for the argument
											option.Value = argument;
											// Kill the argument to prevent further chewing
											argument = "";
										}

										// Get the value into the option
										else option.Value = NextValue(arguments);

										// Handle option not found
										if (option.Value == null)
										{
											// Show an error for value required
											ValueRequiredError(option);
											// Return null due to irrecoverable error
											return null;
										}
										break;
									#endregion
								}
						}
					}

				}
				#endregion

				#region Bare value
				else
				{
					// Add this value to the list of parameters
					Parameters.Add(argument);
				}
				#endregion
			}

			// We didn't fail!
			Failed = false;
			// Convert the parameters list to an array and return it
			return Parameters.ToArray();
		}

		public void ShowHelp()
		{
			// The maximum width of the display
			int displayWidth = 80;
			// The width of the longest option
			int longestOption = 0;
			// Get the longest option
			foreach (Option o in options) longestOption = Math.Max(o.LongOption.Length, longestOption);
			// The indent for the help text will be a little more than the longest 
			// option to accommodate the short option and some padding
			int indentWidth = longestOption + 7;
			// Get the name of the exe
			String exe = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
			// The header
			System.Console.WriteLine("{0} supports the following options: ", exe);

			// Print usage information for each option
			foreach (Option option in options)
			{
				// Write the short option
				if (option.ShortOption != 0) System.Console.Write("-{0} ", option.ShortOption);
				else System.Console.Write("   ");
				// Write the long option
				System.Console.Write("--{0,-" + longestOption + "}", option.LongOption);
				// Initialize the line length to the indent level
				int lineLength = indentWidth;

				// Go through all the words in the help text
				foreach (String word in option.HelpText.Split(' '))
				{
					// If we need to wrap now, then output a newline and indent
					if ((lineLength + word.Length + 1) > displayWidth)
					{
						// Reset the line length
						lineLength = indentWidth;
						// Newline and indent
						System.Console.Write("\n".PadRight(indentWidth));
					}

					// Output the current word
					System.Console.Write(" " + word);
					// Increment the line length by the space and word
					lineLength += word.Length + 1;
				}

				// End this line of help text
				System.Console.WriteLine();
			}
		}

		/// <summary>
		/// Show the usage line for this application set of options.
		/// 
		/// This will output something like the following:
		/// usage: foo.exe -abc -d [value] -e &lt;value&gt;
		/// </summary>
		public void ShowUsage()
		{
			// Get the name of the current exe
			String exe = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
			// Show the usage prefix with the name of the exe
			System.Console.Write("usage: {0}", exe);
			// What will be used to pad the next argument
			String glue = " -";

			// Print each of the options
			foreach (Option option in options)
			{
				// See what kind of value this option can have
				switch (option.ValuePresence)
				{
					case Option.ValueEnum.Optional:
						// Write the info for this option
						System.Console.Write("{0}{1} [value]", glue, option.ShortOption);
						// Next glue will need a dash, because it's gotta get out of the [value]
						glue = " -";
						break;

					case Option.ValueEnum.Prohibited:
						// Write the info for this option
						System.Console.Write("{0}{1}", glue, option.ShortOption);
						// Next glue won't need anything (needs stuck to the previous entry)
						glue = "";
						break;

					case Option.ValueEnum.Required:
						// Write the info for this option
						System.Console.Write("{0}{1} <value>", glue, option.ShortOption);
						// Next glue will need a dash, because it's gotta get out of the <value> 
						glue = " -";
						break;
				}
			}

			// Newline
			System.Console.WriteLine("");
			// If there is a --help option, then tell the user about it
			if (options.Find(o => { return o.LongOption == "help"; }) != null)
				System.Console.WriteLine("Use --help for more information");
		}

		/// <summary>
		/// Subscript operator to return the value of specific options
		/// </summary>
		/// <param name="index">The long name of the option to return</param>
		/// <returns>The value of the long option</returns>
		public Object this[String index]
		{
			get
			{
				// Find the option with this name and return the value
				Option option = options.Find(o => { return o.LongOption == index; });
				// The item wasn't found
				if (option == null) throw new IndexOutOfRangeException();
				// Return value
				return option.Value ?? option.DefaultValue;
			}

			set
			{
				// Set the value of the option having this name
				options.Find(o => { return o.LongOption == index; }).Value = value;
			}
		}

		public override string ToString()
		{
			String result = "";

			foreach (Option option in options)
				result += option.ShortOption.ToString() + "/" + option.LongOption + ": " + option.Value + "\n";

			return result;
		}
		#endregion
	}


	/// <summary>
	/// Encapsulates one of the available options for the command line parser
	/// </summary>
	class Option
	{
		/// <summary>
		/// An enumeration to determine whether or not a value is allowed for this option
		/// </summary>
		public enum ValueEnum
		{
			Prohibited, Optional, Required
		}
		public Char ShortOption;
		public String LongOption;
		public String HelpText;
		public ValueEnum ValuePresence = ValueEnum.Prohibited;
		public Action<Options, Queue<String>> Callback = null;
		public Object Value = null;
		public Object DefaultValue = null;

		/// <summary>
		/// Default constructor
		/// 
		/// Long option is required, because it's an easy thing to provide and makes 
		/// the program much clearer.
		/// </summary>
		/// <param name="longOption">The long option such as "help"</param>
		public Option(String longOption)
		{
			// Store the long option
			this.LongOption = longOption;
		}
	}
}
