using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace Resultful.Examples
{
    public class Error
    {
        public Error(string error)
        {

        }
    }

    public class Examples: Controller
    {
        #region Build
        private Result<int, Error> ParseInt(string input)
        {
            if (input?.All(char.IsDigit) ?? false)
            {
                return int.Parse(input);
            }
            return new Error($"{input} contains a character which is not a digit");
        }
        #endregion

        #region Pipeline
        private Result<int, Error> NumberIsGreaterThanZero(string input)
        {
            if (input == null)
            {
                return new Error("Cannot return int value given is null");
            }
            return ParseInt(input).Bind<int>(x =>
            {
                if (x <= 0)
                {
                    return new Error($"{x} must be a value greater than or equal to 0");
                }
                return x;
            });
        }
        #endregion

        #region Consumption

        private IActionResult Get(string input)
            => NumberIsGreaterThanZero(input)
                .Match<IActionResult>(
                    success => Ok(success),
                    error => BadRequest(error));

        #endregion

    }

    public class Examples2
    {
        private Result<int> ParseInt(string input)
        {
            if (input?.All(char.IsDigit) ?? false)
            {
                return int.Parse(input).Ok();
            }
            return $"{input} contains a character which is not a digit";
        }

        public Result<int> GetAndValidateNumberOfPeople(string people)
        {
            if (people == null)
            {
                return "Cannot return int value given is null";
            }
            return ParseInt(people).Bind(x =>
            {
                if (x <= 0)
                {
                    return $"{x} must be a value ";
                }
                return x.Ok();
            });
        }
    }

}
